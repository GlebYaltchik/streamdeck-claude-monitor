using System.Globalization;
using System.Text.Json;
using ClaudeDeck.Agent;
using ClaudeDeck.Core.Sessions;
using ClaudeDeck.Protocol;

// The agent listens for Claude Code hook events on loopback, records them, and keeps the
// current state of every live session. It decides nothing: every response is empty, so a
// session behaves exactly as it would without it.
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
var staleAfter = Silence("CLAUDEDECK_STALE_AFTER_MINUTES", 120);
var forgetAfter = Silence("CLAUDEDECK_FORGET_AFTER_MINUTES", 720);

var events = new EventLog();
var sessions = new SessionRegistry();
var hub = new HubClient(sessions, HubToken.Read(), Console.WriteLine);
var context = new ContextTracker(sessions, Console.WriteLine);
context.Changed += hub.Publish;
var liveness = new LivenessMonitor(sessions, staleAfter, forgetAfter, Console.WriteLine);
liveness.Changed += hub.Publish;

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
})));

app.MapPost("/hook/{hookEvent}", async (string hookEvent, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();

    try
    {
        events.Append(hookEvent, payload);
        Track(hookEvent, payload);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"could not handle {hookEvent}: {ex.Message}");
    }

    // No content, so the hook sees empty output and forms no opinion about the tool call.
    return Results.NoContent();
});

Console.WriteLine($"ClaudeDeck agent on http://127.0.0.1:{port}");
Console.WriteLine($"recording to {events.Path}");

using var stopping = new CancellationTokenSource();
app.Lifetime.ApplicationStopping.Register(stopping.Cancel);
var reporting = hub.RunAsync(stopping.Token);
var tracking = context.RunAsync(stopping.Token);
var watching = liveness.RunAsync(stopping.Token);

app.Run();
await Task.WhenAll(reporting, tracking, watching);
return;

static TimeSpan Silence(string variable, int fallbackMinutes) =>
    TimeSpan.FromMinutes(
        Environment.GetEnvironmentVariable(variable) is { } value &&
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) &&
        minutes > 0
            ? minutes
            : fallbackMinutes);

void Track(string name, string payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload);
        if (HookEvent.Parse(name, document.RootElement, DateTimeOffset.UtcNow) is { } parsed)
        {
            sessions.Apply(parsed);
            hub.Publish();
        }
    }
    catch (JsonException)
    {
        // An unreadable payload is already in the log; it just cannot move the state machine.
    }
}
