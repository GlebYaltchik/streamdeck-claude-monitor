using ClaudeDeck.Agent;

// The agent listens for Claude Code hook events on loopback and records them. It decides
// nothing: every response is empty, so a session behaves exactly as it would without it.

const int DefaultPort = 17800;

var port = Environment.GetEnvironmentVariable("CLAUDEDECK_AGENT_PORT") is { } configured &&
           int.TryParse(configured, out var parsed)
    ? parsed
    : DefaultPort;

var events = new EventLog();

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Logging.ClearProviders();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", log = events.Path }));

app.MapPost("/hook/{hookEvent}", async (string hookEvent, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();

    try
    {
        events.Append(hookEvent, payload);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"could not record {hookEvent}: {ex.Message}");
    }

    // No content, so the hook sees empty output and forms no opinion about the tool call.
    return Results.NoContent();
});

Console.WriteLine($"ClaudeDeck agent on http://127.0.0.1:{port}");
Console.WriteLine($"recording to {events.Path}");

app.Run();
