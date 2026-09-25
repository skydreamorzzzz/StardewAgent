using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;

namespace StardewAgentBridge;

internal sealed class OperationHost
{
    private sealed class MutableRecord
    {
        public object Gate { get; } = new();
        public OperationRequest Request { get; init; } = null!;
        public string SemanticPayloadHash { get; init; } = "";
        public string Status { get; set; } = "accepted";
        public string? Outcome { get; set; }
        public string? Error { get; set; }
        public ulong? SubmittedTick { get; set; }
        public ulong? SettledTick { get; set; }
        public string? EffectStatus { get; set; }
        public bool? Quiescent { get; set; }
        public int? StartTileX { get; set; }
        public int? StartTileY { get; set; }
        public int? EndTileX { get; set; }
        public int? EndTileY { get; set; }
        public ulong? TicksUsed { get; set; }
        public long? ElapsedMs { get; set; }
        public string? Postcondition { get; set; }
    }

    private readonly string actorId;
    private readonly ActionHandlers handlers;
    private readonly ActorDriver driver;
    private readonly ObservationProjector projector;
    private readonly ConcurrentDictionary<Guid, MutableRecord> records = new();
    private readonly ConcurrentQueue<Guid> ready = new();
    private MovementController? activeMovement;
    private Guid? activeOperationId;
    private volatile int controlEpoch;
    private volatile bool dispatchEnabled = true;

    public OperationHost(string actorId, ActionHandlers handlers, ActorDriver driver, ObservationProjector projector)
    {
        this.actorId = actorId;
        this.handlers = handlers;
        this.driver = driver;
        this.projector = projector;
    }

    public int ControlEpoch => this.controlEpoch;
    public bool DispatchEnabled => this.dispatchEnabled;

    public OperationView Submit(OperationRequest request, ulong tick)
    {
        var semanticHash = ComputeSemanticPayloadHash(request);
        if (this.records.TryGetValue(request.OperationId, out var existing))
            return ResolveDuplicate(existing, semanticHash);

        var validationError = this.ValidateAdmission(request);
        var record = new MutableRecord
        {
            Request = request,
            SemanticPayloadHash = semanticHash,
            SubmittedTick = tick,
            Status = validationError is null ? "accepted" : "rejected",
            Outcome = validationError is null ? null : "rejected",
            Error = validationError,
            SettledTick = validationError is null ? null : tick,
            EffectStatus = validationError is null ? null : "none",
            Quiescent = validationError is null ? null : true,
            Postcondition = validationError is null ? null : "unknown",
        };

        if (!this.records.TryAdd(request.OperationId, record))
            return ResolveDuplicate(this.records[request.OperationId], semanticHash);

        if (validationError is null)
            this.ready.Enqueue(request.OperationId);
        return Snapshot(record);
    }

    public OperationView? Get(Guid operationId)
        => this.records.TryGetValue(operationId, out var record) ? Snapshot(record) : null;

    public ControlView ApplyControl(ControlRequest request, ulong tick)
    {
        switch (request.Kind)
        {
            case "cancel_operation":
                if (request.OperationId is null)
                    throw new InvalidDataException("cancel_operation requires operation_id");
                this.Cancel(request.OperationId.Value, tick);
                break;
            case "pause":
                if (request.NewControlEpoch is int pauseEpoch && pauseEpoch <= this.controlEpoch)
                    throw new InvalidDataException("pause requires a newer control epoch");
                this.controlEpoch = request.NewControlEpoch ?? checked(this.controlEpoch + 1);
                this.dispatchEnabled = false;
                this.activeMovement?.RequestCancel();
                break;
            case "enable":
                if (request.NewControlEpoch is int enableEpoch && enableEpoch != this.controlEpoch)
                    throw new InvalidDataException("enable must acknowledge the current control epoch");
                this.dispatchEnabled = true;
                break;
            case "heartbeat":
                break;
            default:
                throw new InvalidDataException("Unknown control kind: " + request.Kind);
        }

        return new ControlView(
            request.ControlId,
            request.Kind,
            this.controlEpoch,
            this.dispatchEnabled,
            this.activeMovement is null,
            this.activeOperationId is Guid operationId
                ? new[] { operationId }
                : Array.Empty<Guid>());
    }

    public void Tick(ulong tick)
    {
        if (this.activeMovement is not null && this.activeOperationId is Guid activeId)
        {
            var activeRecord = this.records[activeId];
            int operationEpoch;
            lock (activeRecord.Gate)
                operationEpoch = activeRecord.Request.ControlEpoch ?? this.controlEpoch;

            var result = this.activeMovement.Tick(
                this.dispatchEnabled,
                this.controlEpoch,
                operationEpoch);
            if (result is not null)
            {
                ApplyMovementResult(activeRecord, result, tick);
                this.activeMovement = null;
                this.activeOperationId = null;
            }
            return;
        }

        if (!this.ready.TryDequeue(out var id) || !this.records.TryGetValue(id, out var record))
            return;

        lock (record.Gate)
        {
            if (record.Status != "accepted")
                return;
        }

        var validationError = this.ValidateAdmission(record.Request);
        if (validationError is not null)
        {
            Reject(record, validationError, tick);
            return;
        }

        if (record.Request.ActionId == "movement.bounded")
        {
            if (!this.handlers.TryCreateBoundedMove(record.Request, out var spec, out var movementError))
            {
                Reject(record, movementError ?? "INVALID_BOUNDED_MOVE_ARGS", tick);
                return;
            }

            var controller = new MovementController(this.driver, this.projector);
            controller.Start(spec);
            lock (record.Gate)
            {
                record.Status = "running";
                record.EffectStatus = "none";
                record.Quiescent = false;
                record.StartTileX = controller.StartTileX;
                record.StartTileY = controller.StartTileY;
                record.TicksUsed = 0;
                record.ElapsedMs = 0;
            }

            this.activeMovement = controller;
            this.activeOperationId = id;
            var firstTick = controller.Tick(
                this.dispatchEnabled,
                this.controlEpoch,
                record.Request.ControlEpoch ?? this.controlEpoch);
            if (firstTick is not null)
            {
                ApplyMovementResult(record, firstTick, tick);
                this.activeMovement = null;
                this.activeOperationId = null;
            }
            return;
        }

        try
        {
            lock (record.Gate)
                record.Status = "running";

            this.handlers.Execute(record.Request);

            lock (record.Gate)
            {
                record.Status = "settled";
                record.Outcome = "succeeded";
                record.Error = null;
                record.EffectStatus = "unknown";
                record.Postcondition = "unknown";
                record.Quiescent = true;
                record.SettledTick = tick;
            }
        }
        catch (Exception ex)
        {
            lock (record.Gate)
            {
                record.Status = "settled";
                record.Outcome = "failed";
                record.Error = ex.GetType().Name + ": " + ex.Message;
                record.EffectStatus = "unknown";
                record.Postcondition = "unknown";
                record.Quiescent = true;
                record.SettledTick = tick;
            }
        }
    }

    private void Cancel(Guid operationId, ulong tick)
    {
        if (!this.records.TryGetValue(operationId, out var record))
            return;

        if (this.activeOperationId == operationId)
        {
            this.activeMovement?.RequestCancel();
            lock (record.Gate)
                record.Status = "cancel_requested";
            return;
        }

        lock (record.Gate)
        {
            if (record.Status != "accepted")
                return;

            record.Status = "settled";
            record.Outcome = "cancelled";
            record.Error = "CANCELLED_BEFORE_EXECUTION";
            record.EffectStatus = "none";
            record.Postcondition = "unsatisfied";
            record.Quiescent = true;
            record.SettledTick = tick;
        }
    }

    private string? ValidateAdmission(OperationRequest request)
    {
        if (!this.dispatchEnabled)
            return "DISPATCH_PAUSED";
        if (request.ControlEpoch is not null && request.ControlEpoch.Value != this.controlEpoch)
            return "STALE_CONTROL_EPOCH";
        return this.handlers.Validate(request, this.actorId);
    }

    private static void Reject(MutableRecord record, string error, ulong tick)
    {
        lock (record.Gate)
        {
            record.Status = "rejected";
            record.Outcome = "rejected";
            record.Error = error;
            record.EffectStatus = "none";
            record.Postcondition = "unknown";
            record.Quiescent = true;
            record.SettledTick = tick;
        }
    }

    private static void ApplyMovementResult(MutableRecord record, MovementResult result, ulong tick)
    {
        lock (record.Gate)
        {
            record.Status = "settled";
            record.Outcome = result.Outcome;
            record.Error = result.Error;
            record.EffectStatus = result.EffectStatus;
            record.Postcondition = result.Postcondition;
            record.Quiescent = result.Quiescent;
            record.EndTileX = result.EndTileX;
            record.EndTileY = result.EndTileY;
            record.TicksUsed = result.TicksUsed;
            record.ElapsedMs = result.ElapsedMs;
            record.SettledTick = tick;
        }
    }

    private static OperationView ResolveDuplicate(MutableRecord record, string semanticHash)
    {
        lock (record.Gate)
        {
            if (!string.Equals(record.SemanticPayloadHash, semanticHash, StringComparison.Ordinal))
                throw new InvalidDataException("OPERATION_ID_SEMANTIC_MISMATCH");
            return ToViewUnsafe(record);
        }
    }

    private static OperationView Snapshot(MutableRecord record)
    {
        lock (record.Gate)
            return ToViewUnsafe(record);
    }

    private static OperationView ToViewUnsafe(MutableRecord record)
        => new(
            record.Request.OperationId,
            record.Request.ActionId,
            record.Status,
            record.Outcome,
            record.Error,
            record.SubmittedTick,
            record.SettledTick,
            record.EffectStatus,
            record.Quiescent,
            record.StartTileX,
            record.StartTileY,
            record.EndTileX,
            record.EndTileY,
            record.TicksUsed,
            record.ElapsedMs,
            record.Postcondition);

    private static string ComputeSemanticPayloadHash(OperationRequest request)
    {
        var canonical = new StringBuilder();
        canonical.Append(request.ActorId).Append('\n');
        canonical.Append(request.ActionId).Append('\n');
        AppendCanonicalArgs(canonical, request.Args);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static void AppendCanonicalArgs(StringBuilder builder, Dictionary<string, JsonElement>? args)
    {
        builder.Append('{');
        if (args is not null)
        {
            var first = true;
            foreach (var pair in args.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!first)
                    builder.Append(',');
                first = false;
                builder.Append(JsonSerializer.Serialize(pair.Key)).Append(':');
                AppendCanonicalJson(builder, pair.Value);
            }
        }
        builder.Append('}');
    }

    private static void AppendCanonicalJson(StringBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var firstProperty = true;
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!firstProperty)
                        builder.Append(',');
                    firstProperty = false;
                    builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    AppendCanonicalJson(builder, property.Value);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstElement = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstElement)
                        builder.Append(',');
                    firstElement = false;
                    AppendCanonicalJson(builder, item);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString()));
                break;
            case JsonValueKind.Number:
                builder.Append(element.GetRawText());
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                builder.Append("null");
                break;
            default:
                throw new InvalidDataException("Unsupported JSON value in operation args");
        }
    }
}
