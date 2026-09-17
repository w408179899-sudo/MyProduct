using System.Text.Json;
using Smart.Hosting;
using Xunit;

namespace Smart.Runtime.Tests;

public sealed class ConfigContractTests
{
    [Theory]
    [InlineData("-norefresh")]
    [InlineData("-NOREFRESH")]
    public void LiveProfilesCannotDisableProcessLifecycleRefresh(string argument)
    {
        var profile = new AccountProfile("a", RuntimeMode.Hardware,
            new(typeof(AccountProfile).Assembly.Location, "explicit-test-device", [argument], 1, "fixture", "fixture.exe"),
            new("127.0.0.1", 1234, "12345678"));
        var error = Assert.Throws<ArgumentException>(profile.Validate);
        Assert.Contains("-norefresh", error.Message);
    }

    [Theory]
    [InlineData("{\"SchemaVersion\":1,\"Settings\":{\"Accounts\":[{\"Id\":\"a\",\"ProbeIntervaMs\":100}]}}")]
    [InlineData("{\"SchemaVersion\":1,\"Settings\":{\"Accounts\":[]},\"Setings\":{}}")]
    public async Task MisspelledAccountOrDocumentFieldsCannotSilentlyUseDefaults(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "smart-strict-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            var store = new JsonConfigStore<HostSettings>(path, 1, x => x.Validate());
            await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
            Assert.Equal(json, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyFieldIsAcceptedOnlyThroughAnExplicitMigration()
    {
        var path = Path.Combine(Path.GetTempPath(), "smart-migrate-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"SchemaVersion\":1,\"Settings\":{\"LegacyAccount\":\"old\"}}");
            var store = new JsonConfigStore<HostSettings>(path, 2, x => x.Validate(),
                new Dictionary<int, Func<JsonElement, JsonElement>>
                {
                    [1] = old => JsonSerializer.SerializeToElement(new HostSettings([new(old.GetProperty("LegacyAccount").GetString()!)]))
                });
            Assert.Equal("old", Assert.Single((await store.LoadAsync()).Accounts).Id);
        }
        finally { File.Delete(path); }
    }
}
