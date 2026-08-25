using ClaudeDeck.Core.Permissions;
using ClaudeDeck.Core.Rendering;

namespace ClaudeDeck.Plugin.Actions;

/// <summary>
/// One key that says no to the question a session is waiting on.
///
/// The first answer the deck can give, and deliberately the half that cannot wave anything
/// through. Pressed rather than held: a denial costs a retry and nothing else, while allowing
/// is irreversible the moment the command runs — that one is held, in Step 5.
///
/// It answers the question the strip is showing, because both ask the same queue.
///
/// Every way this can fail leaves the question exactly where it was: no agent, no session
/// still waiting, or a mode that is not active. The prompt is on screen in the session's own
/// window the whole time, so doing nothing is always a safe answer.
/// </summary>
internal sealed class DenyAction(
    IDeckConnection connection,
    DeckModes modes,
    PendingQueue queue,
    Func<string, ApprovalDecision, Task<bool>> decide) : IDeckAction
{
    private readonly HashSet<string> _contexts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public string Uuid => "com.gyaltchik.claudedeck.deny";

    public async Task HandleAsync(DeckEvent deckEvent)
    {
        if (deckEvent.Context is null)
        {
            return;
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

            case "keyDown":
                await DenyAsync();
                break;

            case "willDisappear":
                lock (_gate)
                {
                    _contexts.Remove(deckEvent.Context);
                }

                connection.Forget(deckEvent.Context);
                break;
        }
    }

    /// <summary>Redraws every visible key. Safe to call from the hub's own threads.</summary>
    public void Refresh()
    {
        var face = DenyKeyFace.Render(modes.Current, queue.Waiting().Count);

        foreach (var context in Contexts())
        {
            connection.Update(context, new ImageUpdate(face));
        }
    }

    private async Task DenyAsync()
    {
        if (modes.Current != DeckMode.Active)
        {
            PluginLog.Write($"deny ignored, the deck is {DeckModes.Name(modes.Current)}");
            return;
        }

        if (queue.Current() is not { Id.Length: > 0 } asking)
        {
            return;
        }

        var decision = ApprovalDecision.Denied();
        var reached = await decide(asking.Id, decision);
        PluginLog.Write(reached
            ? $"denied {asking.PendingTool} in {asking.Title ?? asking.Id}"
            : $"nothing left to deny in {asking.Title ?? asking.Id}");
    }

    private List<string> Contexts()
    {
        lock (_gate)
        {
            return [.. _contexts];
        }
    }
}
