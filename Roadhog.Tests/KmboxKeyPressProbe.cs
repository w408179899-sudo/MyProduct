using System.Globalization;
using Hardware.KmBox;

internal static class KmboxKeyPressProbe
{
    public static bool ShouldRun(string[] args)
    {
        if (args.Any(arg =>
                string.Equals(arg, "kmbox_press_probe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "kmbox_press", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--kmbox-press-probe", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var mode = Environment.GetEnvironmentVariable("ROADHOG_TEST_MODE")
                   ?? Environment.GetEnvironmentVariable("AION_TEST_MODE");

        return string.Equals(mode, "kmbox_press_probe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, "kmbox_press", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var ip = ReadOption(args, "--ip=", "ROADHOG_KMBOX_IP", "KMBOX_NET_IP", "192.168.2.188");
        var port = ReadIntOption(args, "--port=", "ROADHOG_KMBOX_PORT", "KMBOX_NET_PORT", 4967);
        var mac = ReadOption(args, "--mac=", "ROADHOG_KMBOX_MAC", "KMBOX_NET_MAC", "5BF7E466");
        var keyText = ReadOption(args, "--keys=", "ROADHOG_KMBOX_KEYS", "KMBOX_KEYS", "F2,NumPad1");
        var holdMs = Math.Max(1, ReadIntOption(args, "--hold-ms=", "ROADHOG_KMBOX_HOLD_MS", "KMBOX_HOLD_MS", 60));
        var gapMs = Math.Max(0, ReadIntOption(args, "--gap-ms=", "ROADHOG_KMBOX_GAP_MS", "KMBOX_GAP_MS", 300));

        var keys = keyText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (keys.Length == 0)
        {
            Console.Error.WriteLine("No keys provided.");
            return 2;
        }

        Console.WriteLine("Roadhog KMBox key press probe.");
        Console.WriteLine("Endpoint=" + ip + ":" + port.ToString(CultureInfo.InvariantCulture) +
                          " Mac=" + mac +
                          " Keys=" + string.Join(",", keys) +
                          " HoldMs=" + holdMs.ToString(CultureInfo.InvariantCulture) +
                          " GapMs=" + gapMs.ToString(CultureInfo.InvariantCulture));

        using var device = new KmBoxNetDevice(new KmBoxOptions
        {
            IpAddress = ip,
            Port = port,
            Mac = mac,
            CommandTimeoutMs = 1500,
            SendTimeoutMs = 1500,
            ReceiveTimeoutMs = 1500,
            DefaultClickHoldMs = holdMs,
            TypeKeyDelayMs = gapMs
        });

        if (!await device.ConnectAsync().ConfigureAwait(false))
        {
            Console.Error.WriteLine("KMBox connect failed.");
            return 3;
        }

        try
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (!TryResolveKeyCode(keys[i], out var keyCode))
                {
                    Console.Error.WriteLine("Unsupported key: " + keys[i]);
                    return 4;
                }

                Console.WriteLine("Press " + keys[i] + " code=0x" + keyCode.ToString("X", CultureInfo.InvariantCulture));
                await device.PressKeyAsync(keyCode, holdMs).ConfigureAwait(false);

                if (i + 1 < keys.Length && gapMs > 0)
                {
                    await Task.Delay(gapMs).ConfigureAwait(false);
                }
            }

            Console.WriteLine("KMBoxPressSummary Success=yes");
            return 0;
        }
        finally
        {
            await device.ReleaseAllAsync().ConfigureAwait(false);
        }
    }

    public static bool TryResolveKeyCode(string key, out int keyCode)
    {
        keyCode = 0;
        var normalized = key.Trim();

        if (normalized.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fNumber) &&
            fNumber is >= 1 and <= 12)
        {
            keyCode = KmBoxKeyCodes.KEY_F1 + fNumber - 1;
            return true;
        }

        if (normalized.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized["NumPad".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numPadNumber) &&
            numPadNumber is >= 0 and <= 9)
        {
            keyCode = numPadNumber == 0
                ? KmBoxKeyCodes.KEY_KEYPAD_0_INSERT
                : KmBoxKeyCodes.KEY_KEYPAD_1_END + numPadNumber - 1;
            return true;
        }

        if (normalized.StartsWith("D", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var digit) &&
            digit is >= 0 and <= 9)
        {
            keyCode = digit == 0
                ? KmBoxKeyCodes.KEY_0_CPARENTHESIS
                : KmBoxKeyCodes.KEY_1_EXCLAMATION_MARK + digit - 1;
            return true;
        }

        switch (normalized.ToUpperInvariant())
        {
            case "C":
                keyCode = KmBoxKeyCodes.KEY_C;
                return true;
            case "TAB":
                keyCode = KmBoxKeyCodes.KEY_TAB;
                return true;
            case "SPACE":
                keyCode = KmBoxKeyCodes.KEY_SPACEBAR;
                return true;
            default:
                return false;
        }
    }

    private static string ReadOption(
        string[] args,
        string argumentPrefix,
        string primaryEnvironmentName,
        string fallbackEnvironmentName,
        string defaultValue)
    {
        var arg = args.FirstOrDefault(value => value.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (arg is not null)
        {
            return arg[argumentPrefix.Length..];
        }

        return Environment.GetEnvironmentVariable(primaryEnvironmentName)
               ?? Environment.GetEnvironmentVariable(fallbackEnvironmentName)
               ?? defaultValue;
    }

    private static int ReadIntOption(
        string[] args,
        string argumentPrefix,
        string primaryEnvironmentName,
        string fallbackEnvironmentName,
        int defaultValue)
    {
        var text = ReadOption(args, argumentPrefix, primaryEnvironmentName, fallbackEnvironmentName, string.Empty);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }
}
