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
    private long startedAt;
    private string? startLocation;
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

    public void Start(BoundedMoveSpec spec)
    {
        this.spec = spec;
        this.startedAt = Stopwatch.GetTimestamp();
        var observation = this.projector.Project();
        this.startLocation = observation.Location;
        this.startTileX = observation.TileX;
        this.startTileY = observation.TileY;
        this.ticksUsed = 0;
        this.cancelRequested = false;
    }

    public void RequestCancel()
    {
        this.cancelRequested = true;
    }

    public MovementResult? Tick(bool dispatchEnabled, int controlEpoch, int operationEpoch)
    {
        if (this.spec is null)
            throw new InvalidOperationException("MovementController was not started");

        if (this.cancelRequested || !dispatchEnabled || controlEpoch != operationEpoch)
            return this.FinishInterrupted("cancelled", "MOVEMENT_STOPPED");

        if (!ContextReady())
            return this.FinishInterrupted("failed", "WORLD_NOT_READY");

        if (Game1.activeClickableMenu is not null)
            return this.FinishInterrupted("failed", "MENU_OPEN");

        if (this.ticksUsed >= (ulong)this.spec.MaxTicks || this.ElapsedMilliseconds() >= this.spec.MaxDurationMs)
            return this.FinishForBound();

        this.driver.PressMovement(this.spec.Button);
        this.ticksUsed++;
        return null;
    }

    private MovementResult FinishForBound()
    {
        var observation = this.projector.Project();
        var movedAsRequested = this.HasExpectedDirectionalMovement(observation);
        return this.Finish(
            movedAsRequested ? "succeeded" : "unknown",
            movedAsRequested ? "all" : "unknown",
            movedAsRequested ? "satisfied" : "unknown",
            null,
            observation.TileX,
            observation.TileY);
    }

    private MovementResult FinishInterrupted(string noEffectOutcome, string error)
    {
        var observation = this.projector.Project();
        var movedAsRequested = this.HasExpectedDirectionalMovement(observation);
        return this.Finish(
            movedAsRequested ? "partial" : noEffectOutcome,
            movedAsRequested ? "some" : "none",
            movedAsRequested ? "satisfied" : "unsatisfied",
            error,
            observation.TileX,
            observation.TileY);
    }

    private MovementResult Finish(
        string outcome,
        string effectStatus,
        string postcondition,
        string? error,
        int? endTileX,
        int? endTileY)
    {
        // PressMovement is one-tick injection. Reaching this terminal path means
        // the controller will not inject another movement tick, so it is quiescent.
        return new MovementResult(
            outcome,
            effectStatus,
            postcondition,
            true,
            endTileX,
            endTileY,
            this.ticksUsed,
            this.ElapsedMilliseconds(),
            error);
    }

    private bool HasExpectedDirectionalMovement(ObservationView observation)
    {
        if (this.spec is null ||
            this.startLocation is null ||
            observation.Location is null ||
            !string.Equals(this.startLocation, observation.Location, StringComparison.Ordinal) ||
            !this.startTileX.HasValue ||
            !this.startTileY.HasValue ||
            !observation.TileX.HasValue ||
            !observation.TileY.HasValue)
        {
            return false;
        }

        var dx = observation.TileX.Value - this.startTileX.Value;
        var dy = observation.TileY.Value - this.startTileY.Value;

        return this.spec.Direction switch
        {
            "left" => dx < 0 && dy == 0,
            "right" => dx > 0 && dy == 0,
            "up" => dy < 0 && dx == 0,
            "down" => dy > 0 && dx == 0,
            _ => false,
        };
    }

    private long ElapsedMilliseconds()
        => this.startedAt == 0
            ? 0
            : (long)((Stopwatch.GetTimestamp() - this.startedAt) * 1000.0 / Stopwatch.Frequency);

    private static bool ContextReady()
        => Context.IsWorldReady && Game1.player is not null;
}
