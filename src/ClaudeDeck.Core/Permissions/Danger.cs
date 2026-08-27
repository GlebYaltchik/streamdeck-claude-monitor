using System.Text.RegularExpressions;

namespace ClaudeDeck.Core.Permissions;

/// <summary>
/// Whether allowing a request is worth making somebody work for.
///
/// It errs towards saying yes. The cost of a false alarm is a longer press; the cost of the
/// opposite is a key running the one command that could not be taken back, which is the whole
/// reason design §6.4 exists. So a shape that merely resembles something destructive counts.
///
/// It reads the same one line the key shows, which is what ToolInputs pulled out
/// of the tool's input — the command for a shell call, the path for a file one. That is a
/// deliberate limit: the deck never sees the full input, so this cannot be a sandbox and must
/// not be sold as one. It is a tripwire on the obvious shapes, and the question stays on
/// screen in the session's own window for anything that needs reading properly.
/// </summary>
public static class Danger
{
    /// <summary>
    /// Shapes that destroy, escalate, or hand the machine to something fetched from a network.
    /// Matched against the command with its whitespace collapsed, so "rm    -rf" is the same
    /// thing as "rm -rf".
    /// </summary>
    private static readonly string[] Commands =
    [
        "rm -r",
        "rm -f",
        "rmdir /s",
        "sudo ",
        "doas ",
        "git push --force",
        "git push -f",
        "git reset --hard",
        "git clean -f",
        "chmod 777",
        "chown -r",
        "mkfs",
        "dd if=",
        "shutdown",
        "reboot",
        "truncate -s 0",
        "> /dev/sd",
        "npm publish",
        "docker system prune",
    ];

    /// <summary>
    /// Anything that reads like a credential. A path is enough: a command that names one is
    /// either about to read it or about to overwrite it, and neither should be waved through
    /// from across a desk.
    /// </summary>
    private static readonly string[] Secrets =
    [
        ".env",
        ".ssh",
        "id_rsa",
        "id_ed25519",
        ".pem",
        ".p12",
        ".pfx",
        "credentials",
        "secret",
        ".aws",
        ".npmrc",
        ".gnupg",
        ".kube",
        "token",
    ];

    /// <summary>
    /// A pipe into an interpreter: whatever came out of the network is about to be run. The
    /// classic is <c>curl … | sh</c>, and every variant of it is the same thing.
    /// </summary>
    private static readonly Regex PipedToShell = new(
        @"\|\s*(sudo\s+)?(sh|bash|zsh|ksh|fish|python[0-9.]*|perl|ruby|node)\b",
        RegexOptions.Compiled);

    /// <summary>Whether the deck should make this one harder to allow.</summary>
    /// <param name="cwd">
    /// The session's working directory, when it is known. A write outside it is the one rule
    /// here that cannot be read off the command alone.
    /// </param>
    public static bool Suspects(string? tool, string? summary, string? cwd)
    {
        var line = Collapse(summary);

        if (line.Length == 0)
        {
            // A tool whose input said nothing recognisable. Nothing to read, so nothing to
            // suspect: the key still shows the tool, and the session still shows the rest.
            return false;
        }

        var lower = line.ToLowerInvariant();

        return Commands.Any(shape => lower.Contains(shape, StringComparison.Ordinal)) ||
               Secrets.Any(shape => lower.Contains(shape, StringComparison.Ordinal)) ||
               PipedToShell.IsMatch(lower) ||
               WritesOutside(tool, line, cwd);
    }

    /// <summary>
    /// A file tool pointed somewhere other than the directory the session is working in. The
    /// path is compared as text rather than resolved: the deck may be on a different machine
    /// from the session, and a path that only resolves over there cannot be checked here.
    /// </summary>
    private static bool WritesOutside(string? tool, string line, string? cwd)
    {
        if (!Writes(tool) || cwd is not { Length: > 0 })
        {
            return false;
        }

        // A relative path climbing out of the working directory. Where it lands cannot be
        // known from here, so it counts.
        if (line.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        return Rooted(line) && !Normalise(line).StartsWith(Normalise(cwd), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Writes(string? tool) =>
        tool is "Edit" or "Write" or "NotebookEdit" or "MultiEdit";

    /// <summary>An absolute path, in either family: <c>/etc/hosts</c>, <c>C:\Windows</c>, <c>~/.ssh</c>.</summary>
    private static bool Rooted(string path) =>
        path.StartsWith('/') ||
        path.StartsWith('~') ||
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':');

    /// <summary>
    /// One separator and no trailing one, so a Windows path written either way compares as
    /// itself. Case is left alone here and handled by the comparison.
    /// </summary>
    private static string Normalise(string path) =>
        path.Replace('\\', '/').TrimEnd('/');

    private static string Collapse(string? text) =>
        text is null
            ? string.Empty
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
