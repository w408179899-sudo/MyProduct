using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Core.Accounts;

namespace Roadhog.Infrastructure.Config;

public sealed class JsonGroceryShopSuccessStore(string directory) : IGroceryShopSuccessStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string FileFor(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId))) + ".json");
    }
    public async Task<DateTimeOffset?> LoadAsync(string accountId, CancellationToken token = default)
    {
        var file = FileFor(accountId);
        if (!File.Exists(file)) return null;
        return JsonSerializer.Deserialize<DateTimeOffset>(await File.ReadAllTextAsync(file, token));
    }
    public async Task RecordAsync(string accountId, DateTimeOffset soldOutAt, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        string? temporary = null;
        try
        {
            var previous = await LoadAsync(accountId, token);
            if (previous >= soldOutAt) return;
            Directory.CreateDirectory(directory);
            var file = FileFor(accountId);
            temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(soldOutAt), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }
}
