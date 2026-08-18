using System.Text.Json;
using ClaudeDeck.Agent;
using ClaudeDeck.Core.Sessions;

// The agent listens for Claude Code hook events on loopback, records them, and keeps the
// current state of every live session. It decides nothing: every response is empty, so a
// session behaves exactly as it would without it.

const int DefaultPort = 17800;

var port = Environment.GetEnvironmentVariable("CLAUDEDECK_AGENT_PORT") is { } configured &&
           int.TryParse(configured, out var parsed)
    ? parsed
    : DefaultPort;

var events = new EventLog();
var sessions = new SessionRegistry();

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

app.Run();
return;

void Track(string name, string payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload);
        if (HookEvent.Parse(name, document.RootElement, DateTimeOffset.UtcNow) is { } parsed)
        {
            sessions.Apply(parsed);
        }
    }
    catch (JsonException)
    {
        // An unreadable payload is already in the log; it just cannot move the state machine.
    }
}
