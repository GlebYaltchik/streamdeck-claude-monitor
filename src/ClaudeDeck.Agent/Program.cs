using System.Globalization;
using System.Text.Json;
using ClaudeDeck.Agent;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Protocol;

// The agent listens for Claude Code hook events on loopback, records them, and keeps the
// current state of every live session. It decides nothing: every response is empty, so a
// session behaves exactly as it would without it.
//
// A permission question is the one event it does not answer at once. That request is held
// open while the question is on screen, because the client closes it when the question is
// answered, and that close is the only thing that says so.
//
// It also follows each session's transcript for how full its context is, and reports the
// lot to the hub inside the plugin, while working the same way when the hub is not there.

const int DefaultPort = 17800;

var port = Environment.GetEnvironmentVariable("CLAUDEDECK_AGENT_PORT") is { } configured &&
           int.TryParse(configured, out var parsed)
    ? parsed
    : DefaultPort;

// Both are long because silence is weak evidence: with no process id to ask, a session that
// is merely idle looks exactly like one whose terminal was closed. Short enough to be worth
// having, long enough not to grey out a session its owner is still thinking about, and
// overridable so either can be cut to seconds when testing on the device.
var staleAfter = Minutes("CLAUDEDECK_STALE_AFTER_MINUTES", 120);
var forgetAfter = Minutes("CLAUDEDECK_FORGET_AFTER_MINUTES", 720);

// How long a permission question is held open. Long, because holding costs nothing while the
// question is on screen anyway, and because a connection dropped inside the hold is the only
// thing that tells us the question was answered. It must stay well under the hook's own
// timeout in settings, or a client giving up would look exactly like an answer.
var approvalHold = Minutes("CLAUDEDECK_APPROVAL_HOLD_MINUTES", 15);

var events = new EventLog();
var sessions = new SessionRegistry();
var approvals = new PendingApprovals(sessions, approvalHold, Console.WriteLine);
var hub = new HubClient(sessions, approvals.Resolve, HubToken.Read(), Console.WriteLine);
var context = new ContextTracker(sessions, Console.WriteLine);
context.Changed += hub.Publish;
var liveness = new LivenessMonitor(sessions, staleAfter, forgetAfter, Console.WriteLine);
liveness.Changed += hub.Publish;
approvals.Changed += hub.Publish;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Logging.ClearProviders();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", log = events.Path }));

app.MapGet("/sessions", () => Results.Ok(sessions.Snapshot().Select(session => new
{
    id = session.Id,
    state = session.State.ToString(),
    project = session.Project,
    cwd = session.Cwd,
    transcriptPath = session.TranscriptPath,
    permissionMode = session.PermissionMode,
    currentTool = session.CurrentTool,
    subagentRuns = session.SubagentRuns,
    startedAt = session.StartedAt,
    lastEventAt = session.LastEventAt,
    title = session.Title,
    model = session.Model,
    branch = session.Branch,
    contextTokens = session.Context?.Tokens,
    contextWindow = session.Context?.Window,
    contextPercent = session.Context?.Percent,
    contextEstimated = session.Context?.Estimated,
    awaitingUser = session.AwaitingUser,
    pendingTool = session.Pending?.Tool,
    pendingSummary = session.Pending?.Summary,
})));

app.MapPost("/hook/{hookEvent}", async (string hookEvent, HttpRequest request, CancellationToken abandoned) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync(abandoned);

    HookEvent? tracked = null;
    try
    {
        events.Append(hookEvent, payload);
        tracked = Parse(hookEvent, payload);

        if (tracked is not null)
        {
            sessions.Apply(tracked);
            hub.Publish();
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"could not handle {hookEvent}: {ex.Message}");
    }

    // A permission question is held open while it is on screen, which is how the agent
    // learns it was answered: the client drops the connection when it is.
    if (tracked is not null && approvals.Holds(tracked))
    {
        if (await approvals.HoldAsync(tracked.SessionId, abandoned) is { } decision)
        {
            return Results.Text(decision.ToHookOutput(), "application/json");
        }
    }

    // No content, so the hook sees empty output and forms no opinion about the tool call.
    return Results.NoContent();
});

// Started before anything is announced. The banner used to come first, so an agent that lost
// the port still said it had one - and started detached, the bind exception went nowhere at
// all and the process simply vanished. That is not hypothetical: WSL2 publishes a
// distribution's listening port onto the Windows loopback, so an agent inside WSL on this port
// takes it from the Windows one (findings/wsl-agent.md).
try
{
    app.Start();
}
catch (IOException ex)
{
    Console.Error.WriteLine($"could not listen on http://127.0.0.1:{port}: {ex.Message}");
    Console.Error.WriteLine(
        "Another agent already has it, or WSL is publishing a distribution's port onto the " +
        $"Windows loopback. Give one of them another port with CLAUDEDECK_AGENT_PORT.");
    return 1;
}

Console.WriteLine($"ClaudeDeck agent on http://127.0.0.1:{port}");
Console.WriteLine($"recording to {events.Path}");

using var stopping = new CancellationTokenSource();
app.Lifetime.ApplicationStopping.Register(stopping.Cancel);
var reporting = hub.RunAsync(stopping.Token);
var tracking = context.RunAsync(stopping.Token);
var watching = liveness.RunAsync(stopping.Token);

app.WaitForShutdown();
await Task.WhenAll(reporting, tracking, watching);
return 0;

static TimeSpan Minutes(string variable, int fallbackMinutes) =>
    TimeSpan.FromMinutes(
        Environment.GetEnvironmentVariable(variable) is { } value &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) &&
        minutes > 0
            ? minutes
            : fallbackMinutes);

static HookEvent? Parse(string name, string payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload);
        return HookEvent.Parse(name, document.RootElement, DateTimeOffset.UtcNow);
    }
    catch (JsonException)
    {
        // An unreadable payload is already in the log; it just cannot move the state machine.
        return null;
    }
}
