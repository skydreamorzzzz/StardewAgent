using StardewModdingAPI;
using StardewValley;
using System.Text.Json;

namespace StardewAgentBridge;

internal sealed class ActionHandlers
{
    private readonly ActorDriver driver;

    public ActionHandlers(ActorDriver driver)
    {
        this.driver = driver;
    }

    public string? Validate(OperationRequest request, string actorId)
    {
        if (!string.Equals(request.ActorId, actorId, StringComparison.Ordinal))
            return "UNSUPPORTED_ACTOR";
        if (!Context.IsWorldReady || Game1.player is null)
            return "WORLD_NOT_READY";
        if (Game1.activeClickableMenu is not null && request.ActionId.StartsWith("input.move_", StringComparison.Ordinal))
            return "MENU_OPEN";
        return request.ActionId switch
        {
            "movement.bounded" => this.TryCreateBoundedMove(request, out _, out var boundedError) ? null : boundedError,
            "input.move_up" or
            "input.move_right" or
            "input.move_down" or
            "input.move_left" or
            "input.action" or
            "input.use_tool" => null,
            _ => "UNKNOWN_ACTION"
        };
    }

    public bool TryCreateBoundedMove(OperationRequest request, out BoundedMoveSpec spec, out string? error)
    {
        spec = null!;
        error = null;
        if (request.Args is null ||
            !request.Args.TryGetValue("direction", out var directionValue) ||
            directionValue.ValueKind != JsonValueKind.String)
        {
            error = "INVALID_BOUNDED_MOVE_ARGS";
            return false;
        }

        var direction = directionValue.GetString()?.ToLowerInvariant();
        if (direction is not ("up" or "down" or "left" or "right"))
        {
            error = "INVALID_BOUNDED_MOVE_DIRECTION";
            return false;
        }

        if (!TryGetBoundedInt(request.Args, "max_ticks", 1, 120, out var maxTicks) ||
            !TryGetBoundedInt(request.Args, "max_duration_ms", 50, 5000, out var maxDurationMs))
        {
            error = "INVALID_BOUNDED_MOVE_LIMIT";
            return false;
        }

        spec = new BoundedMoveSpec(direction, maxTicks, maxDurationMs, this.GetMovementButton(direction));
        return true;
    }

    public void Execute(OperationRequest request)
    {
        var button = request.ActionId switch
        {
            "input.move_up" => Game1.options.moveUpButton[0].ToSButton(),
            "input.move_right" => Game1.options.moveRightButton[0].ToSButton(),
            "input.move_down" => Game1.options.moveDownButton[0].ToSButton(),
            "input.move_left" => Game1.options.moveLeftButton[0].ToSButton(),
            "input.action" => Game1.options.actionButton[0].ToSButton(),
            "input.use_tool" => Game1.options.useToolButton[0].ToSButton(),
            _ => throw new InvalidOperationException($"Unsupported action {request.ActionId}")
        };

        this.driver.Press(button);
    }

    private SButton GetMovementButton(string direction)
        => direction switch
        {
            "up" => Game1.options.moveUpButton[0].ToSButton(),
            "right" => Game1.options.moveRightButton[0].ToSButton(),
            "down" => Game1.options.moveDownButton[0].ToSButton(),
            "left" => Game1.options.moveLeftButton[0].ToSButton(),
            _ => throw new InvalidOperationException($"Unsupported movement direction {direction}")
        };

    private static bool TryGetBoundedInt(
        Dictionary<string, JsonElement> args,
        string name,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        return args.TryGetValue(name, out var raw) &&
               raw.ValueKind == JsonValueKind.Number &&
               raw.TryGetInt32(out value) &&
               value >= minimum &&
               value <= maximum;
    }
}
