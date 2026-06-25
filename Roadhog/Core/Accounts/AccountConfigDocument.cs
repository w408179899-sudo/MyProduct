namespace Roadhog.Core.Accounts;

public sealed class AccountConfigDocument
{
    public int Version { get; set; } = 1;

    public List<AccountConfig> Accounts { get; set; } = new();
}
