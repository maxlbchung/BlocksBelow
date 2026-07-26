using UnityEngine;

/// <summary>
/// The typeface the menus are set in, for the panels that build their own UI at runtime and
/// so have no inspector slot to point at the .ttf the way the main menu scene does.
///
/// It lives under Resources for that reason - loading it any other way would mean a serialized
/// reference on a component that is created from code and never exists as an asset.
/// </summary>
public static class MenuFont
{
    private const string FontResourcePath = "Fonts/HennyPenny-Regular";

    // Resources.Load goes to disk on a miss, and the menus ask for this once per label they
    // build, so the result is held rather than looked up each time.
    private static Font cached;

    public static Font Regular
    {
        get
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<Font>(FontResourcePath);
            if (cached == null)
            {
                Debug.LogWarning(
                    $"Menu font not found at Resources/{FontResourcePath}. "
                    + "Falling back to the built-in font.");
                cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return cached;
        }
    }
}
