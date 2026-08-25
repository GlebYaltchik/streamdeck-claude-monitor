using System.Text;
using System.Text.Json;
using ClaudeDeck.Core.Rendering;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Tests;

public class PendingRequestTests
{
    /// <summary>
    /// A real payload carries both, and the command is what the permission is about. The
    /// description is the model's account of it and would read as if it were the command.
    /// </summary>
    [Fact]
    public void The_command_is_preferred_to_the_description()
    {
        var summary = Summarise("""
            {"command":"git push --force","description":"Push the branch"}
            """);

        Assert.Equal("git push --force", summary);
    }

    [Fact]
    public void A_tool_with_no_command_falls_back_to_what_it_does_have()
    {
        Assert.Equal("src/Program.cs", Summarise("""{"file_path":"src/Program.cs"}"""));
        Assert.Equal("https://example.com", Summarise("""{"url":"https://example.com"}"""));
        Assert.Null(Summarise("""{"limit":20}"""));
    }

    /// <summary>
    /// A heredoc would otherwise take over a key that draws one line at a time.
    /// </summary>
    [Fact]
    public void A_command_over_several_lines_becomes_one()
    {
        Assert.Equal("cat <<EOF hello EOF", Summarise("""{"command":"cat <<EOF\nhello\nEOF"}"""));
    }

    [Fact]
    public void A_waiting_key_names_the_tool_and_shows_the_command()
    {
        var face = SessionKeyFace.Render(new SessionSlotFace(
            SessionState.WaitingApproval,
            Title: "deck plugin",
            Project: "streamdeck",
            ContextPercent: 40,
            ContextEstimated: false,
            PendingTool: "Bash",
            PendingSummary: "git push --force"));

        var svg = Decode(face);
        Assert.Contains(">Bash<", svg);
        Assert.Contains("git push", svg);
    }

    /// <summary>
    /// A key that can answer and a key that only reports must be told apart without reading:
    /// the words are the same and only the colour differs. Movement is not used for it,
    /// because movement already means the attention swell and two moving things say neither.
    /// </summary>
    [Fact]
    public void A_key_that_can_answer_differs_only_in_colour()
    {
        var asking = new SessionSlotFace(
            SessionState.WaitingApproval,
            Title: "deck plugin",
            Project: "streamdeck",
            ContextPercent: 40,
            ContextEstimated: false,
            PendingTool: "Bash",
            PendingSummary: "git push --force");

        var reporting = Decode(SessionKeyFace.Render(asking));
        var answering = Decode(SessionKeyFace.Render(asking, answerable: true));

        Assert.NotEqual(reporting, answering);
        Assert.Equal(Words(reporting), Words(answering));
    }

    /// <summary>
    /// Both waiting faces swell, so the colour that tells them apart has to survive the whole
    /// breath. A shared peak would rub it out twice every two and a half seconds.
    /// </summary>
    [Fact]
    public void The_two_waiting_faces_stay_apart_at_the_top_of_the_swell()
    {
        var asking = new SessionSlotFace(
            SessionState.WaitingApproval,
            Title: "deck plugin",
            Project: "streamdeck",
            ContextPercent: 40,
            ContextEstimated: false,
            PendingTool: "Bash",
            PendingSummary: "git push --force");

        foreach (var glow in new[] { 0.0, 0.5, 1.0 })
        {
            Assert.NotEqual(
                Decode(SessionKeyFace.Render(asking, glow)),
                Decode(SessionKeyFace.Render(asking, glow, answerable: true)));
        }
    }

    private static string Words(string svg) =>
        string.Concat(svg.Split('>').Where(part => part.Contains('<')).Select(part => part.Split('<')[0]));

    /// <summary>
    /// The strip says who, not what. Measured on the device: a command at a size that fits
    /// one dial's segment cannot be read at arm's length, and half a command is worse than
    /// none.
    /// </summary>
    [Fact]
    public void The_strip_names_the_session_and_not_the_command()
    {
        var idle = ApprovalStrip.Render(null, null);
        Assert.Equal("none waiting", idle.Value);

        var asking = ApprovalStrip.Render("deck plugin", "Bash");
        Assert.Equal("APPROVALS · Bash", asking.Title);
        Assert.Equal("deck plugin", asking.Value);
    }

    private static string? Summarise(string toolInput) =>
        ToolInputs.Summarise(JsonDocument.Parse(toolInput).RootElement);

    private static string Decode(string dataUrl)
    {
        const string prefix = "data:image/svg+xml;base64,";
        Assert.StartsWith(prefix, dataUrl);
        return Encoding.UTF8.GetString(Convert.FromBase64String(dataUrl[prefix.Length..]));
    }
}
