namespace Roadhog.Core.Accounts;

public sealed class AccountConfigDocument
{
    internal const int V2CombatDefaultVersion = 2;

    // A document without a version predates the V2 default.
    public int Version { get; set; } = 1;

    public List<AccountConfig> Accounts { get; set; } = new();
}
