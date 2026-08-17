using ClaudeDeck.Plugin.Actions;

namespace ClaudeDeck.Plugin;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var arguments = StreamDeckArguments.Parse(args);
        if (arguments is null)
        {
            PluginLog.Write("launched without Stream Deck arguments, exiting");
            return 1;
        }

        PluginLog.Write($"starting on port {arguments.Port}");

        await using var connection = new StreamDeckConnection(arguments);
        var actions = new IDeckAction[]
        {
            new PlaceholderAction(connection),
        }.ToDictionary(action => action.Uuid, StringComparer.Ordinal);

        connection.EventReceived += deckEvent =>
        {
            if (deckEvent.Action is not null && actions.TryGetValue(deckEvent.Action, out var action))
            {
                return action.HandleAsync(deckEvent);
            }

            return Task.CompletedTask;
        };

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            await connection.RunAsync(shutdown.Token);
        }
        catch (Exception ex)
        {
            PluginLog.Write($"fatal: {ex}");
            return 1;
        }

        PluginLog.Write("stopped");
        return 0;
    }
}
