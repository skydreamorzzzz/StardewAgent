using StardewModdingAPI;
using StardewModdingAPI.Events;
using System.Security.Cryptography;

namespace StardewAgentBridge;

public sealed class ModEntry : Mod
{
    private PublicServer? server;
    private OperationHost? operations;

    public override void Entry(IModHelper helper)
    {
        var config = helper.ReadConfig<ModConfig>();
        if (string.IsNullOrWhiteSpace(config.Token))
        {
            config.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            helper.WriteConfig(config);
            this.Monitor.Log("Generated a local Bridge token in config.json.", LogLevel.Info);
        }

        var projector = new ObservationProjector(config.ActorId);
        var driver = new ActorDriver(helper);
        var handlers = new ActionHandlers(driver);
        this.operations = new OperationHost(config.ActorId, handlers);
        this.server = new PublicServer(config.Port, config.Token, config.ActorId, projector, this.operations, this.Monitor);

        helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
        try
        {
            this.server.Start();
            this.Monitor.Log($"Stardew Agent Bridge listening on http://127.0.0.1:{config.Port}/ for actor '{config.ActorId}'.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to start Stardew Agent Bridge: {ex}", LogLevel.Error);
        }
    }

    private void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
    {
        var tick = unchecked((ulong)e.Ticks);
        this.server?.Tick(tick);
        this.operations?.Tick(tick);
    }

}
