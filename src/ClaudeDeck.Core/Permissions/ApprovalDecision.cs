using System.Text.Json;

namespace ClaudeDeck.Core.Permissions;

/// <summary>
/// An answer to a permission question, in the shape the hook protocol takes it.
///
/// <c>allow</c> and <c>deny</c> are the two the client honours. The names are the client's,
/// not ours, so what travels between the deck and the agent is what will be printed.
/// </summary>
public sealed record ApprovalDecision(string Behaviour, string? Message)
{
    public const string Deny = "deny";

    public const string Allow = "allow";

    /// <summary>
    /// The reason a denial carries. A deck cannot type, so it is canned — and it says who
    /// denied it, because the model's next move depends on whether it was refused by a rule
    /// or by a person who is watching.
    /// </summary>
    public static ApprovalDecision Denied() =>
        new(Deny, "Denied on the Stream Deck. Stop and wait for the user.");

    /// <summary>
    /// What the hook prints. The field names are the client's own and are not interchangeable
    /// with the <c>PreToolUse</c> ones: an unknown field fails schema validation for the whole
    /// answer, which the client reads as no decision at all (findings/holding-a-hook.md).
    ///
    /// Serialised rather than interpolated. JSON ending in several closing braces cannot be
    /// written in a raw string literal without counting dollar signs, and the counting is
    /// where this went wrong once already.
    /// </summary>
    public string ToHookOutput() => Behaviour == Deny && Message is { Length: > 0 }
        ? JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = Event,
                decision = new { behavior = Deny, message = Message },
            },
        })
        : JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = Event,
                decision = new { behavior = Behaviour },
            },
        });

    private const string Event = "PermissionRequest";
}
