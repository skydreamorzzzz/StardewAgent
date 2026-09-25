using StardewModdingAPI;

namespace StardewAgentBridge;

internal sealed class ActorDriver
{
    private readonly IModHelper helper;
    private SButton? movementButton;

    public ActorDriver(IModHelper helper)
    {
        this.helper = helper;
    }

    public void Press(SButton button)
    {
        this.helper.Input.Press(button);
    }

    public void PressMovement(SButton button)
    {
        this.helper.Input.Press(button);
        this.movementButton = button;
    }

    public void StopMovement()
    {
        // SMAPI Press injects one tick at a time; clearing this marker guarantees
        // the controller will not inject another movement input after stopping.
        this.movementButton = null;
    }
}
