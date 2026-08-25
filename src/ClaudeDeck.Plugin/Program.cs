using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Hub;
using ClaudeDeck.Plugin.Actions;
using ClaudeDeck.Protocol;

namespace ClaudeDeck.Plugin;

internal static class Program
{
    /// <summary>
    /// How often visible keys are redrawn. The endpoint is not polled this often — the
    /// provider's cache decides that — but the reset countdown has to keep moving.
    /// </summary>
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// One frame of a swelling slot. The device has no animated image format the plugin can
    /// use, so movement is frames — see findings/streamdeck.md.
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(80);


    private static async Task<int> Main(string[] args)
    {
        var arguments = StreamDeckArguments.Parse(args);
        if (arguments is null)
        {
            PluginLog.Write("launched without Stream Deck arguments, exiting");
            return 1;
        }

        PluginLog.Write($"starting on port {arguments.Port}");

        using var usage = new UsageService();
        await using var hub = new HubServer(new HubOptions { Token = HubToken.ReadOrCreate(PluginLog.Write), Log = PluginLog.Write });
        await using var connection = new StreamDeckConnection(arguments);

        var alerts = new Alerts();
        var modes = new DeckModes();
        var usageAction = new UsageAction(connection, usage);
        var summaryAction = new SummaryAction(connection, hub.Agents);
        var sessionAction = new SessionAction(connection, hub.Agents, alerts, hub.ForgetSessionAsync);
        var alertAction = new AlertAction(connection, alerts, () => sessionAction.Waiting());
        var modeAction = new ModeAction(connection, modes);
        var queue = new PendingQueue(hub.Agents);
        var approvalAction = new ApprovalAction(connection, queue);
        var denyAction = new DenyAction(
            connection,
            modes,
            queue,
            (session, decision) => hub.DecideAsync(session, decision.Behaviour, decision.Message));
        var actions = new IDeckAction[]
        {
            usageAction,
            summaryAction,
            sessionAction,
            alertAction,
            modeAction,
            approvalAction,
            denyAction,
        }.ToDictionary(action => action.Uuid, StringComparer.Ordinal);

        hub.Agents.Changed += summaryAction.Refresh;
        hub.Agents.Changed += sessionAction.Refresh;
        hub.Agents.Changed += alertAction.Refresh;
        hub.Agents.Changed += approvalAction.Refresh;
        hub.Agents.Changed += denyAction.Refresh;

        // Muting has to reach the slots as well as the key that did it.
        alerts.Changed += sessionAction.Refresh;
        alerts.Changed += alertAction.Refresh;

        // The mode is the agents' business as much as the key's: with the deck off they stop
        // holding questions open, which is what makes the switch a real one.
        modes.Changed += modeAction.Refresh;

        // The deny key says whether it can do anything, and the mode is half of that answer.
        modes.Changed += denyAction.Refresh;
        modes.Changed += () => _ = TellAgentsAsync(hub, modes);

        connection.EventReceived += deckEvent =>
            deckEvent.Action is not null && actions.TryGetValue(deckEvent.Action, out var action)
                ? action.HandleAsync(deckEvent)
                : Task.CompletedTask;

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        var redrawing = RedrawAsync(usageAction, shutdown.Token);
        var pulsing = PulseAsync(sessionAction, shutdown.Token);
        var serving = hub.RunAsync(shutdown.Token);

        try
        {
            await connection.RunAsync(shutdown.Token);
        }
        catch (Exception ex)
        {
            PluginLog.Write($"fatal: {ex}");
            return 1;
        }
        finally
        {
            await shutdown.CancelAsync();
            await redrawing;
            await pulsing;
            await serving;
        }

        PluginLog.Write("stopped");
        return 0;
    }

    /// <summary>
    /// Passes the mode to every connected agent. Detached from the key press on purpose: a
    /// key must not wait on a websocket, and an agent that misses the message is told again
    /// when it reconnects.
    /// </summary>
    private static async Task TellAgentsAsync(HubServer hub, DeckModes modes)
    {
        try
        {
            await hub.SetModeAsync(DeckModes.Name(modes.Current));
        }
        catch (Exception ex)
        {
            PluginLog.Write($"could not send the mode: {ex.Message}");
        }
    }

    private static async Task PulseAsync(SessionAction sessionAction, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FrameInterval, cancellationToken);
                await sessionAction.PulseAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                PluginLog.Write($"pulse failed: {ex.Message}");
            }
        }
    }

    private static async Task RedrawAsync(UsageAction usageAction, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RedrawInterval, cancellationToken);
                await usageAction.RefreshAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                PluginLog.Write($"redraw failed: {ex.Message}");
            }
        }
    }
}
