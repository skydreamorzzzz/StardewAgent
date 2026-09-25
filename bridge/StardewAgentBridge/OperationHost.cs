using System.Collections.Concurrent;
using StardewModdingAPI;

namespace StardewAgentBridge;

internal sealed class OperationHost
{
    private sealed class MutableRecord
    {
        public OperationRequest Request { get; init; } = null!;
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
    private int controlEpoch;
    private bool dispatchEnabled = true;

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
        if (this.records.TryGetValue(request.OperationId, out var existing))
            return ToView(existing);

        var validationError = this.ValidateAdmission(request);
        var record = new MutableRecord
        {
            Request = request,
            SubmittedTick = tick,
            Status = validationError is null ? "accepted" : "rejected",
            Outcome = validationError is null ? null : "rejected",
            Error = validationError,
            SettledTick = validationError is null ? null : tick,
            EffectStatus = validationError is null ? null : "none",
            Quiescent = validationError is null ? null : true,
        };

        if (!this.records.TryAdd(request.OperationId, record))
            return ToView(this.records[request.OperationId]);
        if (validationError is null)
            this.ready.Enqueue(request.OperationId);
        return ToView(record);
    }

    public OperationView? Get(Guid operationId)
        => this.records.TryGetValue(operationId, out var record) ? ToView(record) : null;

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
                this.controlEpoch = request.NewControlEpoch ?? checked(this.controlEpoch + 1);
                this.dispatchEnabled = false;
                this.activeMovement?.RequestCancel();
                break;
            case "enable":
                if (request.NewControlEpoch is not null && request.NewControlEpoch.Value < this.controlEpoch)
                    throw new InvalidDataException("Cannot enable an older control epoch");
                if (request.NewControlEpoch is not null)
                    this.controlEpoch = request.NewControlEpoch.Value;
                this.dispatchEnabled = true;
                break;
            case "heartbeat":
                break;
            default:
                throw new InvalidDataException($"Unknown control kind: {request.Kind}");
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
            var result = this.activeMovement.Tick(
                tick,
                this.dispatchEnabled,
                this.controlEpoch,
                this.records[activeId].Request.ControlEpoch ?? this.controlEpoch);
            if (result is not null)
            {
                ApplyMovementResult(this.records[activeId], result, tick);
                this.activeMovement = null;
                this.activeOperationId = null;
            }
            return;
        }

        if (!this.ready.TryDequeue(out var id) || !this.records.TryGetValue(id, out var record))
            return;
        if (record.Status != "accepted")
            return;

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
            controller.Start(spec, tick);
            record.Status = "running";
            record.EffectStatus = "none";
            record.Quiescent = false;
            record.StartTileX = controller.StartTileX;
            record.StartTileY = controller.StartTileY;
            record.TicksUsed = 0;
            record.ElapsedMs = 0;
            this.activeMovement = controller;
            this.activeOperationId = id;
            var firstTick = controller.Tick(tick, this.dispatchEnabled, this.controlEpoch, record.Request.ControlEpoch ?? this.controlEpoch);
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
            record.Status = "running";
            this.handlers.Execute(record.Request);
            record.Status = "settled";
            record.Outcome = "succeeded";
            record.Error = null;
            record.EffectStatus = "unknown";
            record.Postcondition = "unknown";
            record.Quiescent = true;
            record.SettledTick = tick;
        }
        catch (Exception ex)
        {
            record.Status = "settled";
            record.Outcome = "failed";
            record.Error = $"{ex.GetType().Name}: {ex.Message}";
            record.EffectStatus = "unknown";
            record.Quiescent = true;
            record.SettledTick = tick;
        }
    }

    private void Cancel(Guid operationId, ulong tick)
    {
        if (!this.records.TryGetValue(operationId, out var record))
            return;
        if (this.activeOperationId == operationId)
        {
            this.activeMovement?.RequestCancel();
            record.Status = "cancel_requested";
            return;
        }
        if (record.Status == "accepted")
        {
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
        record.Status = "rejected";
        record.Outcome = "rejected";
        record.Error = error;
        record.EffectStatus = "none";
        record.Postcondition = "unknown";
        record.Quiescent = true;
        record.SettledTick = tick;
    }

    private static void ApplyMovementResult(MutableRecord record, MovementResult result, ulong tick)
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

    private static OperationView ToView(MutableRecord record)
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
}
