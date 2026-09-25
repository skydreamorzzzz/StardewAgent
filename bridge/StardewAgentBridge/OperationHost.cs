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
    }

    private readonly string actorId;
    private readonly ActionHandlers handlers;
    private readonly ConcurrentDictionary<Guid, MutableRecord> records = new();
    private readonly ConcurrentQueue<Guid> ready = new();

    public OperationHost(string actorId, ActionHandlers handlers)
    {
        this.actorId = actorId;
        this.handlers = handlers;
    }

    public OperationView Submit(OperationRequest request, ulong tick)
    {
        if (this.records.TryGetValue(request.OperationId, out var existing))
            return ToView(existing);

        var validationError = this.handlers.Validate(request, this.actorId);
        var record = new MutableRecord
        {
            Request = request,
            SubmittedTick = tick,
            Status = validationError is null ? "accepted" : "rejected",
            Outcome = validationError is null ? null : "rejected",
            Error = validationError,
            SettledTick = validationError is null ? null : tick,
        };

        if (!this.records.TryAdd(request.OperationId, record))
            return ToView(this.records[request.OperationId]);

        if (validationError is null)
            this.ready.Enqueue(request.OperationId);

        return ToView(record);
    }

    public OperationView? Get(Guid operationId)
        => this.records.TryGetValue(operationId, out var record) ? ToView(record) : null;

    public void Tick(ulong tick)
    {
        // v0: one primitive side effect per game tick. This keeps the first bridge easy to reason about.
        if (!this.ready.TryDequeue(out var id) || !this.records.TryGetValue(id, out var record))
            return;

        if (record.Status != "accepted")
            return;

        var validationError = this.handlers.Validate(record.Request, this.actorId);
        if (validationError is not null)
        {
            record.Status = "rejected";
            record.Outcome = "rejected";
            record.Error = validationError;
            record.SettledTick = tick;
            return;
        }

        try
        {
            record.Status = "running";
            this.handlers.Execute(record.Request);

            // IMPORTANT: this proves only that the bounded input was injected.
            // It does not claim that a crop was watered or that movement succeeded.
            record.Status = "settled";
            record.Outcome = "succeeded";
            record.SettledTick = tick;
        }
        catch (Exception ex)
        {
            record.Status = "settled";
            record.Outcome = "failed";
            record.Error = $"{ex.GetType().Name}: {ex.Message}";
            record.SettledTick = tick;
        }
    }

    private static OperationView ToView(MutableRecord r)
        => new(r.Request.OperationId, r.Request.ActionId, r.Status, r.Outcome, r.Error, r.SubmittedTick, r.SettledTick);
}
