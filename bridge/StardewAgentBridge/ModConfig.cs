namespace StardewAgentBridge;

internal sealed class ModConfig
{
    public int Port { get; set; } = 8765;
    public string Token { get; set; } = "";
    public string ActorId { get; set; } = "agent_player";
}
