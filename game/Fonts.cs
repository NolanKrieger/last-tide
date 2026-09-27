using Godot;

namespace LastTide;

/// <summary>
/// The IM Fell family (SIL OFL, licences beside each file in assets/fonts): one face per role so every screen letters
/// the same way. Body is the long-standing IM Fell English Roman (also <see cref="Main.Fell"/>).
/// </summary>
public static class Fonts
{
    static FontFile? body, italic, smallCaps, display, displayItalic, displayCaps;

    static FontFile Load(ref FontFile? slot, string file) => slot ??= GD.Load<FontFile>("res://assets/fonts/" + file);

    /// <summary>IM Fell English Roman: running text, numbers, labels.</summary>
    public static FontFile Body => Load(ref body, "IMFellEnglish-Roman.ttf");
    /// <summary>IM Fell English Italic: flavour lines, log entries, asides.</summary>
    public static FontFile Italic => Load(ref italic, "IMFellEnglish-Italic.ttf");
    /// <summary>IM Fell English SC: section heads, tabs, column captions.</summary>
    public static FontFile SmallCaps => Load(ref smallCaps, "IMFellEnglishSC-Regular.ttf");
    /// <summary>IM Fell French Canon Roman: big titles (cut for large sizes; don't use it under ~28 px).</summary>
    public static FontFile Display => Load(ref display, "IMFellFrenchCanon-Regular.ttf");
    /// <summary>IM Fell French Canon Italic: big flourished titles.</summary>
    public static FontFile DisplayItalic => Load(ref displayItalic, "IMFellFrenchCanon-Italic.ttf");
    /// <summary>IM Fell French Canon SC: the game's name, cartouche titles.</summary>
    public static FontFile DisplayCaps => Load(ref displayCaps, "IMFellFrenchCanonSC-Regular.ttf");
}
