using StardewModdingAPI;

namespace StardewAgentBridge;

internal sealed class ActorDriver
{
    private readonly IModHelper helper;

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
        // SMAPI Press injects input for the current update tick only. A bounded
        // movement stops by ceasing future Press calls; there is no held key to release.
        this.helper.Input.Press(button);
    }
}
