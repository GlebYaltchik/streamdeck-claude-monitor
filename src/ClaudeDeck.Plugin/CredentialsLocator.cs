using ClaudeDeck.Core.Usage;
using Microsoft.Win32;

namespace ClaudeDeck.Plugin;

/// <summary>
/// Finds Claude Code's credentials file.
///
/// The local profile is not always where it lives: on a Windows host whose sessions run in
/// WSL, the token exists only inside the distribution. Probing for it means the key works
/// without the user first having to discover that and configure a path by hand.
///
/// The share root cannot be enumerated, so distribution names come from the registry.
/// </summary>
internal static class CredentialsLocator
{
    private const string DistributionsKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>
    /// An explicitly configured path is returned as given, even when it does not exist, so a
    /// typo surfaces as "log in" rather than being silently replaced by a different account.
    /// </summary>
    public static string? Locate(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        foreach (var candidate in Candidates())
        {
            if (Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return FileCredentialsStore.DefaultPath();

        foreach (var distribution in Distributions())
        {
            foreach (var home in HomeDirectories(distribution))
            {
                yield return Path.Combine(home, ".claude", ".credentials.json");
            }
        }
    }

    private static IEnumerable<string> Distributions()
    {
        List<string> names = [];
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(DistributionsKey);
            if (root is null)
            {
                return names;
            }

            foreach (var id in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(id);
                if (entry?.GetValue("DistributionName") is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // No WSL, or no permission to look. Neither is an error worth surfacing.
        }

        return names;
    }

    private static IEnumerable<string> HomeDirectories(string distribution)
    {
        var root = $@"\\wsl.localhost\{distribution}";
        yield return Path.Combine(root, "root");

        string[] users;
        try
        {
            users = Directory.GetDirectories(Path.Combine(root, "home"));
        }
        catch
        {
            // A stopped distribution simply has nothing to offer.
            yield break;
        }

        foreach (var user in users)
        {
            yield return user;
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
