namespace Roadhog.Infrastructure.Input;

public static class RoadhogInputKeyMap
{
    private static readonly IReadOnlyDictionary<string, int> HidCodes =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = 0x06,
            ["W"] = 0x1A,
            ["X"] = 0x1B,
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
            ["OemMinus"] = 0x2D,
            ["OemPlus"] = 0x2E,
            ["OemComma"] = 0x36,
            ["Tab"] = 0x2B,
            ["NumPad1"] = 0x59,
            ["NumPad2"] = 0x5A,
            ["NumPad3"] = 0x5B,
            ["NumPad4"] = 0x5C,
            ["NumPad5"] = 0x5D,
            ["NumPad6"] = 0x5E,
            ["NumPad7"] = 0x5F,
            ["NumPad8"] = 0x60,
            ["NumPad9"] = 0x61,
            ["NumPad0"] = 0x62
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
