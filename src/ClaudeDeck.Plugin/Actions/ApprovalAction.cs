using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// The encoder that shows what a session is waiting to be allowed to do.
///
/// One question at a time: the oldest one still waiting, which is the one that has held its
/// session up longest. Several at once is Step 6, and the strip is deliberately built before
/// any key can answer — a deck that can decide before it can show what it is deciding about
/// is the thing design §6.4 exists to prevent.
/// </summary>
internal sealed class ApprovalAction(IDeckConnection connection, PendingQueue queue) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.approval";

    public Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return Task.CompletedTask;
        }

        switch (deckEvent.Name)
        {
            case "willAppear":
                lock (_gate)
                {
                    _contexts.Add(deckEvent.Context);
                }

                Refresh();
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _contexts.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>Redraws every visible strip. Safe to call from the hub's own threads.</summary>
    public void Refresh()
    {
        var asking = queue.Current();

        var strip = ApprovalStrip.Render(asking?.Title, asking?.PendingTool);

        foreach (var context in Contexts())
        {
            connection.Update(context, new FeedbackUpdate(strip.Title, strip.Value, 0));
        }
    }

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _contexts];
        }
    }
}
