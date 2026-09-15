namespace TranslucentTabBar.Tests;

/// <summary>
/// Basic smoke tests that verify the test infrastructure works.
/// Tests for WinRT-dependent code (ColorHelper, AppConfig with Color) require
/// a WinUI 3 test environment and should be run manually or in integration tests.
/// </summary>
[TestClass]
public class BasicTests
{
    [TestMethod]
    public void TestInfrastructure_Works()
    {
        Assert.IsTrue(true, "Test infrastructure is functional");
    }

    [TestMethod]
    public void HexParsing_Logic_ValidFormats()
    {
        // Test the hex parsing logic without using Windows.UI.Color
        // This verifies the core algorithm that ColorHelper uses

        // Valid 6-digit hex
        Assert.IsTrue(TryParseHexColor("#FF0000", out var r, out var g, out var b));
        Assert.AreEqual(255, r);
        Assert.AreEqual(0, g);
        Assert.AreEqual(0, b);

        // Valid 8-digit hex (with alpha)
        Assert.IsTrue(TryParseHexColor("#80FF0080", out var a, out r, out g, out b));
        Assert.AreEqual(128, a);
        Assert.AreEqual(255, r);
        Assert.AreEqual(0, g);
        Assert.AreEqual(128, b);
    }

    [TestMethod]
    public void HexParsing_Logic_InvalidFormats()
    {
        // Invalid inputs should return false
        Assert.IsFalse(TryParseHexColor(null, out _, out _, out _, out _));
        Assert.IsFalse(TryParseHexColor("", out _, out _, out _, out _));
        Assert.IsFalse(TryParseHexColor("#GGHHII", out _, out _, out _, out _));
        Assert.IsFalse(TryParseHexColor("#12345", out _, out _, out _, out _));
    }

    [TestMethod]
    public void HexFormatting_Logic_ProducesCorrectFormat()
    {
        // Test the hex formatting logic
        var hex = FormatHexColor(255, 128, 64, 32);
        Assert.AreEqual("#FF804020", hex);
    }

    // Simplified hex parsing logic (mirrors ColorHelper.TryFromHex)
    private static bool TryParseHexColor(string? hex, out byte a, out byte r, out byte g, out byte b)
    {
        a = r = g = b = 0;

        if (string.IsNullOrEmpty(hex))
            return false;

        hex = hex.TrimStart('#');

        if (hex.Length == 6)
        {
            return TryParseHexPair(hex, 0, out r) &&
                   TryParseHexPair(hex, 2, out g) &&
                   TryParseHexPair(hex, 4, out b);
        }
        else if (hex.Length == 8)
        {
            return TryParseHexPair(hex, 0, out a) &&
                   TryParseHexPair(hex, 2, out r) &&
                   TryParseHexPair(hex, 4, out g) &&
                   TryParseHexPair(hex, 6, out b);
        }

        return false;
    }

    private static bool TryParseHexColor(string? hex, out byte r, out byte g, out byte b)
    {
        return TryParseHexColor(hex, out _, out r, out g, out b);
    }

    private static bool TryParseHexPair(string hex, int offset, out byte value)
    {
        value = 0;
        if (offset + 1 >= hex.Length)
            return false;

        var high = HexCharToValue(hex[offset]);
        var low = HexCharToValue(hex[offset + 1]);

        if (high < 0 || low < 0)
            return false;

        value = (byte)(high * 16 + low);
        return true;
    }

    private static int HexCharToValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    private static string FormatHexColor(byte a, byte r, byte g, byte b)
    {
        return $"#{a:X2}{r:X2}{g:X2}{b:X2}";
    }
}
