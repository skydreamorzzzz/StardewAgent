using System.Diagnostics;
using StardewModdingAPI;
using StardewValley;

namespace StardewAgentBridge;

internal sealed record BoundedMoveSpec(
    string Direction,
    int MaxTicks,
    int MaxDurationMs,
    SButton Button
);

internal sealed record MovementResult(
    string Outcome,
    string EffectStatus,
    string Postcondition,
    bool Quiescent,
    int? EndTileX,
    int? EndTileY,
    ulong TicksUsed,
    long ElapsedMs,
    string? Error
);

internal sealed class MovementController
{
    private readonly ActorDriver driver;
    private readonly ObservationProjector projector;
    private BoundedMoveSpec? spec;
    private ulong startTick;
    private long startedAt;
    private int? startTileX;
    private int? startTileY;
    private ulong ticksUsed;
    private bool cancelRequested;

    public MovementController(ActorDriver driver, ObservationProjector projector)
    {
        this.driver = driver;
        this.projector = projector;
    }

    public int? StartTileX => this.startTileX;
    public int? StartTileY => this.startTileY;
    public ulong TicksUsed => this.ticksUsed;
    public long ElapsedMs => this.ElapsedMilliseconds();

    public void Start(BoundedMoveSpec spec, ulong tick)
    {
        this.spec = spec;
        this.startTick = tick;
        this.startedAt = Stopwatch.GetTimestamp();
        var observation = this.projector.Project();
        this.startTileX = observation.TileX;
        this.startTileY = observation.TileY;
        this.ticksUsed = 0;
        this.cancelRequested = false;
    }

    public void RequestCancel()
    {
        this.cancelRequested = true;
    }

    public MovementResult? Tick(ulong tick, bool dispatchEnabled, int controlEpoch, int operationEpoch)
    {
        if (this.spec is null)
            throw new InvalidOperationException("MovementController was not started");

        if (this.cancelRequested || !dispatchEnabled || controlEpoch != operationEpoch)
            return this.Finish("cancelled", "unknown", "unknown", "MOVEMENT_STOPPED");

        if (!ContextReady())
            return this.Finish("blocked", "none", "unsatisfied", "WORLD_NOT_READY");

        if (Game1.activeClickableMenu is not null)
            return this.Finish("blocked", "none", "unsatisfied", "MENU_OPEN");

        if (this.ticksUsed >= (ulong)this.spec.MaxTicks || this.ElapsedMilliseconds() >= this.spec.MaxDurationMs)
            return this.FinishForBound();

        this.driver.PressMovement(this.spec.Button);
        this.ticksUsed++;
        return null;
    }

    private MovementResult FinishForBound()
    {
        var observation = this.projector.Project();
        var moved = HasMoved(observation.TileX, observation.TileY);
        return this.Finish(
            moved ? "succeeded" : "unknown",
            moved ? "all" : "unknown",
            moved ? "satisfied" : "unknown",
            null,
            observation.TileX,
            observation.TileY);
    }

    private MovementResult Finish(
        string outcome,
        string effectStatus,
        string postcondition,
        string? error,
        int? endTileX = null,
        int? endTileY = null)
    {
        this.driver.StopMovement();
        var observation = this.projector.Project();
        return new MovementResult(
            outcome,
            effectStatus,
            postcondition,
            true,
            endTileX ?? observation.TileX,
            endTileY ?? observation.TileY,
            this.ticksUsed,
            this.ElapsedMilliseconds(),
            error);
    }

    private bool HasMoved(int? endTileX, int? endTileY)
        => this.startTileX.HasValue &&
           this.startTileY.HasValue &&
           endTileX.HasValue &&
           endTileY.HasValue &&
           (this.startTileX.Value != endTileX.Value || this.startTileY.Value != endTileY.Value);

    private long ElapsedMilliseconds()
        => this.startedAt == 0
            ? 0
            : (long)((Stopwatch.GetTimestamp() - this.startedAt) * 1000.0 / Stopwatch.Frequency);

    private static bool ContextReady()
        => Context.IsWorldReady && Game1.player is not null;
}
