using System.Text.Json;
using System.Windows.Forms;
using Hardware.KmBox;

var options = LoadOptions();

try
{
    using var device = new KmBoxNetDevice(options);

    Console.WriteLine("Connecting to KMBox Net " + options.IpAddress + ":" + options.Port + " ...");
    if (!await device.ConnectAsync())
    {
        Console.WriteLine("Connect failed. Check KmBox settings in appsettings.json or KMBOX_* environment variables.");
        return 1;
    }

    Console.WriteLine("Connected. Running smoke test in 2 seconds.");
    await Task.Delay(TimeSpan.FromSeconds(2));

    try
    {
        await device.MoveMouseAsync(80, 0);
        await Task.Delay(150);

        await device.MoveMouseSmoothAsync(-80, 0, 300);
        await Task.Delay(150);

        await device.ClickAsync(MouseButton.Left);
        await Task.Delay(150);

        await device.WheelAsync(1);
        await Task.Delay(150);

        await device.TypeTextAsync("KMBox Net smoke test");
        await Task.Delay(150);

        await device.KeyDownAsync(Keys.Enter);
        await Task.Delay(60);
        await device.KeyUpAsync(Keys.Enter);

        Console.WriteLine("Smoke test finished.");
        return 0;
    }
    finally
    {
        await device.ReleaseAllAsync();
        await device.DisconnectAsync();
        Console.WriteLine("Released all KMBox input states.");
    }
}
catch (ArgumentException ex)
{
    Console.WriteLine("Invalid KMBox config: " + ex.Message);
    Console.WriteLine("Set Hardware.KmBox.Test/appsettings.json or KMBOX_IP, KMBOX_PORT, KMBOX_MAC.");
    return 1;
}

static KmBoxOptions LoadOptions()
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    KmBoxOptions options;

    if (File.Exists(configPath))
    {
        using var stream = File.OpenRead(configPath);
        var document = JsonSerializer.Deserialize<AppSettingsDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        options = document?.KmBox ?? new KmBoxOptions();
    }
    else
    {
        options = new KmBoxOptions();
    }

    ApplyEnvironmentOverrides(options);
    return options;
}

static void ApplyEnvironmentOverrides(KmBoxOptions options)
{
    var ipAddress = Environment.GetEnvironmentVariable("KMBOX_IP");
    if (!string.IsNullOrWhiteSpace(ipAddress))
    {
        options.IpAddress = ipAddress;
    }

    var port = Environment.GetEnvironmentVariable("KMBOX_PORT");
    if (int.TryParse(port, out var parsedPort))
    {
        options.Port = parsedPort;
    }

    var mac = Environment.GetEnvironmentVariable("KMBOX_MAC");
    if (!string.IsNullOrWhiteSpace(mac))
    {
        options.Mac = mac;
    }
}

sealed class AppSettingsDocument
{
    public KmBoxOptions? KmBox { get; set; }
}
