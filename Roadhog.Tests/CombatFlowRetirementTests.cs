using System.Text.Json;
using Roadhog.Infrastructure.Config;

internal static class CombatFlowRetirementTests
{
    public static async Task OldAccountConfigsRoundTripAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-combat-retirement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "accounts.json");
            var store = new JsonAccountConfigStore(path);
            foreach (var versionProperty in new[] { "", "\"Version\": 1,", "\"Version\": 2," })
            {
                var json = $$"""
                    {
                      {{versionProperty}}
                      "Accounts": [
                        { "AccountName": "unset", "CombatPathName": "original-route" },
                        { "AccountName": "legacy-name", "CombatFlowVersion": "Legacy" },
                        { "AccountName": "legacy-number", "CombatFlowVersion": 0 },
                        { "AccountName": "v2", "CombatFlowVersion": "CoordinatedV2" },
                        { "AccountName": "unknown", "CombatFlowVersion": "unknown" }
                      ]
                    }
                    """;
                await File.WriteAllTextAsync(path, json);
                var loaded = await store.LoadAllAsync();
                if (!loaded.Success || loaded.Value is not { Count: 5 } accounts)
                    throw new InvalidOperationException("Retired combat preferences must not prevent loading accounts.");
                if (await File.ReadAllTextAsync(path) != json)
                    throw new InvalidOperationException("Reading must not rewrite the existing config.");

                var account = accounts.Single(item => item.AccountName == "unset");
                if (!(await store.UpsertAsync(account)).Success)
                    throw new InvalidOperationException("An old account must remain editable.");
                using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                var storedAccounts = saved.RootElement.GetProperty("Accounts").EnumerateArray().ToArray();
                if (storedAccounts.Length != accounts.Count ||
                    storedAccounts.Any(item => item.TryGetProperty("CombatFlowVersion", out _)) ||
                    storedAccounts.Single(item => item.GetProperty("AccountName").GetString() == "unset")
                        .GetProperty("CombatPathName").GetString() != "original-route")
                    throw new InvalidOperationException("Saving must preserve accounts and unrelated settings without writing retired preferences.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
