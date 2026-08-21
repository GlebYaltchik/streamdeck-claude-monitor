using System.Globalization;
using ClaudeDeck.Core.Sessions;

namespace ClaudeDeck.Core.Rendering;

/// <summary>What one slot has to show. Assembled from whatever the hub last heard.</summary>
public sealed record SessionSlotFace(
    SessionState State,
    string? Title,
    string? Project,
    int? ContextPercent,
    bool ContextEstimated);


/// <summary>
/// Draws one session slot: what the session is called, what it is doing, and how full its
/// context is.
///
/// The name is the key. Tried on the device first: the project and the branch, which put
/// three sessions from the same repository side by side reading "streamdeck-c…" and "main" —
/// identical and useless. The session's own title is what tells them apart.
///
/// The state is the background. It was the colour of the bar until the bar had nothing to
/// colour: a session at one percent, or one whose context is unknown, showed almost no
/// coloured pixels and its state with them. A background is there whatever the numbers say.
///
/// The context is a bar rather than a ring or a large number. The exact percentage is rarely
/// what is wanted at a glance, and the ring crowded out the text that was. The percentage
/// stays, small, for when it is. Its colour is the severity of the fill, which is the only
/// thing about it worth reacting to.
///
/// An unknown context leaves the bar empty and reads as a dash. Measured reason: a compaction
/// leaves the size unknown until the session's next turn, and a full-looking empty bar would
/// say the context is free — the opposite of what has just happened.
///
/// A slot waiting to be looked at swells and fades. Moving rather than one more colour: the
/// point is to be caught by an eye pointed at a screen rather than at the deck, and a static
/// colour among five other static colours is not. Swelling rather than blinking, because a
/// key that snaps between two states pulls the eye off the work — rejected on the device for
/// exactly that.
///
/// The swell is sent frame by frame, because the device offers no other way: an animated SVG
/// is rasterized once and an animated GIF is shown as a still, both measured. The frames go
/// out unthrottled, and each one is timed from the clock rather than counted, which is what
/// keeps the swell even — see <see cref="SlotPulse"/>.
/// </summary>
public static class SessionKeyFace
{
    // Dark enough that the white name stays readable on every one of them, saturated enough
    // to be told apart at arm's length.
    private const string Working = "#16324f";
    private const string Idle = "#17331f";
    private const string Waiting = "#5c3f12";
    private const string Compacting = "#2e2a4d";
    private const string Gone = "#232830";

    /// <summary>
    /// The top of the swell. Warm enough to be seen coming and dark enough to keep the white
    /// name readable at every point in between, which is what lets the whole face stay put
    /// while only its background moves.
    /// </summary>
    private const string Lit = "#8a5c22";


    // How full is worth reacting to. Below the first there is nothing to do, above the second
    // the session is close to compacting.
    private const int Amber = 60;
    private const int Red = 85;

    private const string Roomy = "#4f9d69";
    private const string Filling = "#e0a03a";
    private const string Full = "#e05c5c";

    /// <summary>
    /// The bar's own track. Darker than <see cref="KeyPalette.Track"/>, which was drawn for
    /// the one fixed background the other keys have: here the background is a state colour
    /// and only something darker than all of them reads as a groove on every key.
    /// </summary>
    private const string Track = "#0f1319";

    private const int Margin = 12;

    private const int NameSize = 22;

    /// <summary>
    /// What fits on one line at <see cref="NameSize"/>. Estimated from the font size rather
    /// than measured — the device is the judge, and this is the number to change.
    /// </summary>
    private const int NameCharacters = 11;

    /// <summary>A slot with nothing in it: a dash, dim enough to ignore.</summary>
    public static string Empty() =>
        new KeyImage()
            .Background(KeyPalette.Background)
            .Text("–", 84, 40, KeyPalette.Dim)
            .ToDataUrl();

    /// <param name="attention">
    /// How far into the swell this slot is, from 0 for the ordinary face to 1 for the top of
    /// it. Only the background moves: everything a key says stays exactly where it was, so
    /// the swell is read at the edge of vision without the face becoming unreadable.
    /// </param>
    public static string Render(SessionSlotFace session, double attention = 0)
    {
        var image = new KeyImage().Background(Blend(Background(session.State), Lit, attention));

        var lines = Wrap(session.Title ?? session.Project ?? "session");
        var top = lines.Count == 1 ? 56 : 42;

        for (var line = 0; line < lines.Count; line++)
        {
            image.Text(lines[line], top + (line * 26), NameSize, KeyPalette.Primary, bold: true);
        }

        var percent = session.ContextPercent;

        return image
            .Bar(percent is { } fill ? fill / 100d : 0, Severity(percent), Track, y: 98, height: 12, margin: Margin)
            .Text(Fill(session), 133, 17, KeyPalette.Muted)
            .ToDataUrl();
    }

    /// <summary>
    /// The percentage, or a dash when nothing is known yet. A trailing <c>?</c> marks a window
    /// that was guessed rather than known, so a number resting on an unrecognised model never
    /// passes for a measured one.
    /// </summary>
    private static string Fill(SessionSlotFace session) => session.ContextPercent switch
    {
        null => "–",
        { } percent when session.ContextEstimated => $"{percent}%?",
        { } percent => $"{percent}%",
    };

    /// <summary>
    /// Mixes two <c>#rrggbb</c> colours. An amount of zero returns the first one unchanged,
    /// which is what keeps a slot that is not swelling byte for byte the face it already had,
    /// and therefore never resent.
    /// </summary>
    private static string Blend(string from, string to, double amount)
    {
        var mix = Math.Clamp(amount, 0, 1);
        if (mix <= 0)
        {
            return from;
        }

        return "#" + string.Concat(Enumerable.Range(0, 3).Select(channel =>
        {
            var start = Channel(from, channel);
            var end = Channel(to, channel);
            return ((int)Math.Round(start + ((end - start) * mix))).ToString("x2", CultureInfo.InvariantCulture);
        }));
    }

    private static int Channel(string colour, int index) =>
        Convert.ToInt32(colour.Substring(1 + (index * 2), 2), 16);

    private static string Background(SessionState state) => state switch
    {
        SessionState.Working => Working,
        SessionState.WaitingApproval => Waiting,
        SessionState.Compacting => Compacting,
        SessionState.Stale => Gone,
        _ => Idle,
    };

    private static string Severity(int? percent) => percent switch
    {
        >= Red => Full,
        >= Amber => Filling,
        _ => Roomy,
    };

    /// <summary>
    /// Breaks a name over at most two lines, on a space where there is one. What will not fit
    /// is cut rather than shrunk: a name too small to read is worth no more than no name.
    /// </summary>
    private static List<string> Wrap(string name)
    {
        var text = name.Trim();
        if (text.Length <= NameCharacters)
        {
            return [text];
        }

        var split = text.LastIndexOf(' ', Math.Min(NameCharacters, text.Length - 1));
        var first = split > 0 ? text[..split] : text[..NameCharacters];
        var rest = text[(split > 0 ? split + 1 : NameCharacters)..].Trim();

        return rest.Length <= NameCharacters
            ? [first, rest]
            : [first, rest[..(NameCharacters - 1)] + "…"];
    }
}
