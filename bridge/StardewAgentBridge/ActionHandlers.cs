using StardewModdingAPI;
using StardewValley;

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
            "input.move_up" or
            "input.move_right" or
            "input.move_down" or
            "input.move_left" or
            "input.action" or
            "input.use_tool" => null,
            _ => "UNKNOWN_ACTION"
        };
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
}
