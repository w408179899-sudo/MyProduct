namespace Roadhog.Infrastructure.Input;

public sealed class KmBoxNetDeviceConfig
{
    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Mac { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(IpAddress) &&
        Port > 0 &&
        !string.IsNullOrWhiteSpace(Mac);

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            error = "KMBox Net IP cannot be empty.";
            return false;
        }

        if (Port <= 0 || Port > 65535)
        {
            error = "KMBox Net port must be between 1 and 65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Mac))
        {
            error = "KMBox Net MAC cannot be empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void ApplyTo(KmBoxNetKeyboardInputOptions options)
    {
        options.IpAddress = IpAddress.Trim();
        options.Port = Port;
        options.Mac = Mac.Trim();
    }

    public static KmBoxNetDeviceConfig FromOptions(KmBoxNetKeyboardInputOptions options)
    {
        return new KmBoxNetDeviceConfig
        {
            IpAddress = options.IpAddress,
            Port = options.Port,
            Mac = options.Mac
        };
    }
}
