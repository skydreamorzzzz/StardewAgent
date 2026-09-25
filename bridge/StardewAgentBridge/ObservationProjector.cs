using StardewModdingAPI;
using StardewValley;

namespace StardewAgentBridge;

internal sealed class ObservationProjector
{
    private readonly string actorId;

    public ObservationProjector(string actorId)
    {
        this.actorId = actorId;
    }

    public ObservationView Project()
    {
        if (!Context.IsWorldReady || Game1.player is null)
        {
            return new ObservationView(
                this.actorId, false,
                null, null, null, null, null,
                null, null, null, null, null, null,
                null, null, Game1.activeClickableMenu?.GetType().Name
            );
        }

        var player = Game1.player;
        return new ObservationView(
            this.actorId,
            true,
            Game1.year,
            Game1.currentSeason,
            Game1.dayOfMonth,
            Game1.timeOfDay,
            Game1.currentLocation?.Name,
            player.TilePoint.X,
            player.TilePoint.Y,
            player.FacingDirection,
            player.Stamina,
            player.health,
            player.Money,
            player.CurrentItem?.Name,
            player.CurrentTool?.Name,
            Game1.activeClickableMenu?.GetType().Name
        );
    }
}
