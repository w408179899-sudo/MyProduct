using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Application.Trading;

namespace Roadhog.Infrastructure.Config;

public sealed class JsonAuctionListingJournal(string directory) : IAuctionListingJournal
{
    private string FileName(string account, string character) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + character))) + ".json");
    public async Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token)
    {
        var file = FileName(account, character);
        if (!File.Exists(file)) return new();
        return JsonSerializer.Deserialize<AuctionListingHistory>(await File.ReadAllTextAsync(file, token)) ?? throw new InvalidDataException("挂售记录为空。");
    }
    public async Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var file = FileName(account, character); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(history), token);
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
