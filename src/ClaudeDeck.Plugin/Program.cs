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
        var roles = new AnswerRoles();
        var usageAction = new UsageAction(connection, usage);
        var summaryAction = new SummaryAction(connection, hub.Agents);
        var sessionAction = new SessionAction(
            connection,
            hub.Agents,
            alerts,
            modes,
            hub.ForgetSessionAsync,
            (session, decision) => hub.DecideAsync(session, decision.Behaviour, decision.Message));
        var alertAction = new AlertAction(connection, alerts, () => sessionAction.Waiting());
        var modeAction = new ModeAction(connection, modes);
        var queue = new PendingQueue(hub.Agents);
        var approvalAction = new ApprovalAction(connection, queue);
        var answerAction = new AnswerAction(connection, modes, roles);
        var actions = new IDeckAction[]
        {
            usageAction,
            summaryAction,
            sessionAction,
            alertAction,
            modeAction,
            approvalAction,
            answerAction,
        }.ToDictionary(action => action.Uuid, StringComparer.Ordinal);

        hub.Agents.Changed += summaryAction.Refresh;
        hub.Agents.Changed += sessionAction.Refresh;
        hub.Agents.Changed += alertAction.Refresh;
        hub.Agents.Changed += approvalAction.Refresh;

        // Muting has to reach the slots as well as the key that did it.
        alerts.Changed += sessionAction.Refresh;
        alerts.Changed += alertAction.Refresh;

        modes.Changed += modeAction.Refresh;
        modes.Changed += () => _ = RememberAsync(connection, modes, roles);

        // The pair is drawn resting when the deck may not answer, and both keys change sides
        // together because there is only one side to change.
        modes.Changed += answerAction.Refresh;
        roles.Changed += answerAction.Refresh;
        roles.Changed += () => _ = RememberAsync(connection, modes, roles);

        // A session key answers only in active mode, and says so by its colour.
        modes.Changed += sessionAction.Refresh;

        connection.EventReceived += deckEvent =>
        {
            // Settings for the plugin belong to no action, so they are read here rather than
            // routed to one. A deck with no Approvals key on it has no other way to be told.
            if (deckEvent.Name == "didReceiveGlobalSettings")
            {
                modes.Set(PluginSettings.Mode(deckEvent.Payload));
                roles.Set(PluginSettings.Swapped(deckEvent.Payload));
                return Task.CompletedTask;
            }

            return deckEvent.Action is not null && actions.TryGetValue(deckEvent.Action, out var action)
                ? action.HandleAsync(deckEvent)
                : Task.CompletedTask;
        };

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
    /// Writes the plugin's settings, so a deck answers the same way after a restart as it did
    /// before one. Detached from the key press for the same reason as the hub once was: a key
    /// must not wait on a socket.
    ///
    /// Both settings go every time. Stream Deck stores what it is given rather than merging
    /// it, so writing one of them alone would erase the other.
    /// </summary>
    private static async Task RememberAsync(IDeckConnection connection, DeckModes modes, AnswerRoles roles)
    {
        try
        {
            await connection.SaveGlobalSettingsAsync(new
            {
                mode = DeckModes.Name(modes.Current),
                swapped = roles.Swapped,
            });
        }
        catch (Exception ex)
        {
            PluginLog.Write($"could not save the settings: {ex.Message}");
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
