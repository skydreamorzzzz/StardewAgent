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
}
