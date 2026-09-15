using Windows.UI;

namespace TranslucentTabBar.Helpers;

/// <summary>
/// Helper methods for color conversion between Windows.UI.Color and hex strings.
/// </summary>
public static class ColorHelper
{
    /// <summary>
    /// Converts a Color to an ARGB hex string (#AARRGGBB).
    /// </summary>
    public static string ToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// Converts a Color to an RGB hex string (#RRGGBB), dropping alpha.
    /// </summary>
    public static string ToRgbHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    /// <summary>
    /// Parses a hex color string (#RGB, #RGBA, #RRGGBB, or #AARRGGBB).
    /// Throws FormatException if the input is not a valid hex color.
    /// </summary>
    public static Color FromHex(string hex)
    {
        if (TryFromHex(hex, out var color))
            return color;

        throw new FormatException($"Invalid hex color: '{hex}'");
    }

    /// <summary>
    /// Tries to parse a hex color string (#RGB, #RGBA, #RRGGBB, or #AARRGGBB).
    /// Returns false if the input is not a valid hex color.
    /// </summary>
    public static bool TryFromHex(string? hex, out Color color)
    {
        color = default;

        if (string.IsNullOrEmpty(hex))
            return false;

        hex = hex.TrimStart('#');

        if (hex.Length == 3) // #RGB
        {
            if (!TryHexCharToByte(hex[0], out var r) ||
                !TryHexCharToByte(hex[1], out var g) ||
                !TryHexCharToByte(hex[2], out var b))
                return false;
            color = Color.FromArgb(255, (byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            return true;
        }
        else if (hex.Length == 4) // #RGBA
        {
            if (!TryHexCharToByte(hex[0], out var a) ||
                !TryHexCharToByte(hex[1], out var r) ||
                !TryHexCharToByte(hex[2], out var g) ||
                !TryHexCharToByte(hex[3], out var b))
                return false;
            color = Color.FromArgb((byte)(a * 17), (byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            return true;
        }
        else if (hex.Length == 6) // #RRGGBB
        {
            if (!TryHexPairToByte(hex, 0, out var r) ||
                !TryHexPairToByte(hex, 2, out var g) ||
                !TryHexPairToByte(hex, 4, out var b))
                return false;
            color = Color.FromArgb(255, r, g, b);
            return true;
        }
        else if (hex.Length == 8) // #AARRGGBB
        {
            if (!TryHexPairToByte(hex, 0, out var a) ||
                !TryHexPairToByte(hex, 2, out var r) ||
                !TryHexPairToByte(hex, 4, out var g) ||
                !TryHexPairToByte(hex, 6, out var b))
                return false;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a Color to a uint in 0xAABBGGRR (ABGR) format (for ACCENT_POLICY GradientColor).
    /// </summary>
    public static uint ToAccentColor(Color color)
    {
        return (uint)((color.A << 24) | (color.B << 16) | (color.G << 8) | color.R);
    }

    /// <summary>
    /// Converts a Color to a uint in 0xAARRGGBB (ARGB) format (for ExplorerTAP XAML brushes).
    /// </summary>
    public static uint ToArgbColor(Color color)
    {
        return (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
    }

    private static bool TryHexCharToByte(char c, out byte value)
    {
        if (c >= '0' && c <= '9') { value = (byte)(c - '0'); return true; }
        if (c >= 'a' && c <= 'f') { value = (byte)(c - 'a' + 10); return true; }
        if (c >= 'A' && c <= 'F') { value = (byte)(c - 'A' + 10); return true; }
        value = 0;
        return false;
    }

    private static bool TryHexPairToByte(string hex, int offset, out byte value)
    {
        if (!TryHexCharToByte(hex[offset], out var high) ||
            !TryHexCharToByte(hex[offset + 1], out var low))
        {
            value = 0;
            return false;
        }
        value = (byte)(high * 16 + low);
        return true;
    }
}
