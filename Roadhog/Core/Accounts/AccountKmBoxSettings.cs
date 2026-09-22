namespace Roadhog.Core.Accounts;

public sealed class AccountKmBoxSettings
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 5000;
    public string Mac { get; set; } = string.Empty;

    public bool Validate(out string error)
    {
        if (!System.Net.IPAddress.TryParse(IpAddress, out _))
        {
            error = "请填写有效的 KMBox IP 地址。";
            return false;
        }
        if (Port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(Mac))
        {
            error = "请填写有效的 KMBox 端口和 MAC。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public AccountKmBoxSettings Clone() => new() { IpAddress = IpAddress, Port = Port, Mac = Mac };
}
