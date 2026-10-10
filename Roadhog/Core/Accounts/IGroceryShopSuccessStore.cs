namespace Roadhog.Core.Accounts;

public interface IGroceryShopSuccessStore
{
    Task<DateTimeOffset?> LoadAsync(string accountId, CancellationToken token = default);
    Task RecordAsync(string accountId, DateTimeOffset soldOutAt, CancellationToken token = default);
}
