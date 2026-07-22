namespace Roadhog.Infrastructure.Input;

public static class RoadhogInputKeyMap
{
    private static readonly IReadOnlyDictionary<string, int> HidCodes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A"] = 0x04,
            ["B"] = 0x05,
            ["C"] = 0x06,
            ["D"] = 0x07,
            ["E"] = 0x08,
            ["F"] = 0x09,
            ["G"] = 0x0A,
            ["H"] = 0x0B,
            ["I"] = 0x0C,
            ["J"] = 0x0D,
            ["K"] = 0x0E,
            ["L"] = 0x0F,
            ["M"] = 0x10,
            ["N"] = 0x11,
            ["O"] = 0x12,
            ["P"] = 0x13,
            ["Q"] = 0x14,
            ["R"] = 0x15,
            ["S"] = 0x16,
            ["T"] = 0x17,
            ["U"] = 0x18,
            ["V"] = 0x19,
            ["W"] = 0x1A,
            ["X"] = 0x1B,
            ["Y"] = 0x1C,
            ["Z"] = 0x1D,
            ["Space"] = 0x2C,
            ["D1"] = 0x1E,
            ["D2"] = 0x1F,
            ["D3"] = 0x20,
            ["D4"] = 0x21,
            ["D5"] = 0x22,
            ["D6"] = 0x23,
            ["D7"] = 0x24,
            ["D8"] = 0x25,
            ["D9"] = 0x26,
            ["D0"] = 0x27,
            ["Oem3"] = 0x35,
            ["Backquote"] = 0x35,
            ["`"] = 0x35,
            ["OemMinus"] = 0x2D,
            ["OemPlus"] = 0x2E,
            ["OemComma"] = 0x36,
            ["Tab"] = 0x2B,
            ["F1"] = 0x3A,
            ["F2"] = 0x3B,
            ["F3"] = 0x3C,
            ["F4"] = 0x3D,
            ["F5"] = 0x3E,
            ["F6"] = 0x3F,
            ["F8"] = 0x41,
            ["F9"] = 0x42,
            ["NumPad1"] = 0x59,
            ["NumPad2"] = 0x5A,
            ["NumPad3"] = 0x5B,
            ["NumPad4"] = 0x5C,
            ["NumPad5"] = 0x5D,
            ["NumPad6"] = 0x5E,
            ["NumPad7"] = 0x5F,
            ["NumPad8"] = 0x60,
            ["NumPad9"] = 0x61,
            ["NumPad0"] = 0x62,
            ["NumPadSubtract"] = 0x56,
            ["NumPadAdd"] = 0x57,
            ["NumPadDecimal"] = 0x63
        };

    public static IReadOnlyCollection<string> SupportedKeys => HidCodes.Keys.ToArray();

    public static IReadOnlyCollection<int> SupportedHidCodes => HidCodes.Values.Distinct().ToArray();

    public static bool TryResolveHidCode(string key, out int hidCode)
    {
        hidCode = 0;
        return !string.IsNullOrWhiteSpace(key) &&
               HidCodes.TryGetValue(key.Trim(), out hidCode);
    }

    public static string FormatSupportedKeys()
    {
        return string.Join(", ", SupportedKeys);
    }
}
