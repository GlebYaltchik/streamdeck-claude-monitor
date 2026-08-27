using ClaudeDeck.Core.Permissions;

namespace ClaudeDeck.Core.Rendering;

/// <summary>
/// Draws one half of the answering pair: Allow or Deny.
///
/// The pair is exactly two keys, and a key that is not part of one says so on its own face
/// rather than waiting silently for a press that will do nothing. A deck is arranged by
/// dragging keys onto it, so the instruction belongs where the mistake is visible — on the
/// key, in the words that fix it.
///
/// The colour is the role and the brightness is how close the key is to being pressed. Off,
/// the words are grey and the key reads as disabled, which is what it is. On, the role
/// colours arrive dark: the pair is ready but nothing has asked it for anything, and the keys
/// that are asking for attention are the session keys — the pair must not compete with them.
/// Full brightness is kept back for the one moment it means something, which is a session
/// addressed and the pair waiting to be pressed.
///
/// Allow and Deny are the one place on this deck where green and red mean what everybody
/// already reads them as.
///
/// A dangerous request therefore cannot be red - red is already Deny, and two red keys side by
/// side say neither. It is the background that turns, which is the rule the session key has
/// used since the beginning: the state is behind the words and the words keep their own
/// meaning. The word stays "allow" and stays green, so the key still says which half of the
/// pair it is while shouting about what it is holding.
///
/// The warning is on Allow alone. It belongs where the permission is given, and Deny gives
/// none.
/// </summary>
public static class AnswerKeyFace
{
    private const string Label = "ANSWER";

    // An addressed session, and the pair waiting to be pressed. The one moment full brightness
    // means something.
    private const string AllowArmed = "#4f9d69";

    private const string DenyArmed = "#e05c5c";

    // The pair on, but nothing addressed. Dark enough to stay out of the way of a session key
    // that is swelling for attention, and still coloured enough to say which key is which.
    private const string AllowResting = "#31543f";

    private const string DenyResting = "#6d3b3b";

    /// <summary>
    /// The pair off. Grey, and no brighter than the resting colours above it: switching
    /// answering on must never look like a key going dimmer, which is what a lighter disabled
    /// grey did on the device.
    /// </summary>
    private const string Disabled = "#414850";

    /// <summary>
    /// Behind a dangerous request. Dark enough that a green word and a light bar stay readable
    /// on it, saturated enough that it is the first thing seen.
    /// </summary>
    private const string DangerBackground = "#5c1d1d";

    private const string DangerLabel = "#ffb3b3";

    /// <param name="keys">
    /// How many answer keys are on the deck. Anything but two is not a pair, and every one of
    /// them says what to do about it.
    /// </param>
    /// <param name="answering">
    /// Whether the deck may answer at all, which is the Approvals mode. Off, the pair is drawn
    /// grey rather than hidden: a key that vanishes when the mode changes leaves nothing to
    /// explain why it stopped working.
    /// </param>
    /// <param name="waiting">
    /// Whether any session is stopped at a question. The instruction appears only when there
    /// is something to follow it with: a key that asks to be tapped when tapping achieves
    /// nothing is a key that stops being read.
    /// </param>
    /// <param name="remaining">
    /// What is left of the addressed session's twenty seconds, from 1 down to 0, or null when
    /// nothing is addressed. It replaces the instruction rather than joining it — the press it
    /// was asking for has happened, and what matters now is how long there is to finish.
    /// </param>
    /// <param name="dangerous">
    /// Whether what is addressed is worth being made to work for. Shown on Allow and nowhere
    /// else: the warning belongs where the permission is given.
    /// </param>
    public static string Render(
        AnswerRole role,
        int keys,
        bool answering,
        bool waiting = false,
        double? remaining = null,
        bool dangerous = false)
    {
        if (keys != 2)
        {
            return Unpaired(keys);
        }

        var armed = answering && remaining is not null;
        var warned = armed && dangerous && role == AnswerRole.Allow;

        var image = new KeyImage()
            .Background(warned ? DangerBackground : KeyPalette.Background)
            .Text(warned ? "DANGER" : Label, 30, 19, warned ? DangerLabel : KeyPalette.Muted, bold: warned)
            .Text(AnswerRoles.Name(role), 82, 30, Word(role, answering, armed), bold: true);

        if (!answering)
        {
            image.Text("watch only", 124, 17, Disabled);
        }
        else if (armed)
        {
            image.Bar(remaining!.Value, KeyPalette.Muted, KeyPalette.Track, y: 116, height: 10, margin: 20);
        }
        else if (waiting)
        {
            image.Text("tap a session", 124, 17, KeyPalette.Dim);
        }

        return image.ToDataUrl();
    }

    private static string Unpaired(int keys) =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text(Label, 30, 19, KeyPalette.Muted)
            .Text("no pair", 82, 28, KeyPalette.Warning, bold: true)
            .Text(keys < 2 ? "add one more" : "keep only two", 124, 17, KeyPalette.Dim)
            .ToDataUrl();

    private static string Word(AnswerRole role, bool answering, bool armed)
    {
        if (!answering)
        {
            return Disabled;
        }

        return armed
            ? role == AnswerRole.Allow ? AllowArmed : DenyArmed
            : role == AnswerRole.Allow ? AllowResting : DenyResting;
    }
}
