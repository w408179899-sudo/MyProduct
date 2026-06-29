using Hardware.KmBox;

namespace Roadhog.Infrastructure.Input;

public sealed class KmBoxNetKeyboardInputOptions
{
    public string IpAddress { get; set; } =
        Environment.GetEnvironmentVariable("KMBOX_NET_IP") ??
        Environment.GetEnvironmentVariable("KMBOX_IP") ??
        string.Empty;

    public int Port { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_PORT") ??
        ReadIntFromEnvironment("KMBOX_PORT") ??
        0;

    public string Mac { get; set; } =
        Environment.GetEnvironmentVariable("KMBOX_NET_MAC") ??
        Environment.GetEnvironmentVariable("KMBOX_MAC") ??
        string.Empty;

    public int CommandTimeoutMs { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_COMMAND_TIMEOUT_MS") ?? 1000;

    public int SendTimeoutMs { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_SEND_TIMEOUT_MS") ?? 1000;

    public int ReceiveTimeoutMs { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_RECEIVE_TIMEOUT_MS") ?? 1000;

    public int DefaultClickHoldMs { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_CLICK_HOLD_MS") ?? 30;

    public int TypeKeyDelayMs { get; set; } =
        ReadIntFromEnvironment("KMBOX_NET_TYPE_KEY_DELAY_MS") ?? 40;

    public KmBoxOptions ToKmBoxOptions()
    {
        return new KmBoxOptions
        {
            IpAddress = IpAddress,
            Port = Port,
            Mac = Mac,
            CommandTimeoutMs = CommandTimeoutMs,
            SendTimeoutMs = SendTimeoutMs,
            ReceiveTimeoutMs = ReceiveTimeoutMs,
            DefaultClickHoldMs = DefaultClickHoldMs,
            TypeKeyDelayMs = TypeKeyDelayMs
        };
    }

    public string EndpointText()
    {
        return string.IsNullOrWhiteSpace(IpAddress)
            ? "(unconfigured)"
            : IpAddress.Trim() + ":" + Port;
    }

    private static int? ReadIntFromEnvironment(string name)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : null;
    }
}
