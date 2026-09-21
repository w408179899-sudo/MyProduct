namespace Roadhog.Application.Trading;

public sealed record AuctionListingReceipt(uint ListingId, uint TemplateId, ulong UnitPrice, DateTimeOffset ListedAt);
public sealed class AuctionListingHistory
{
    public List<AuctionListingReceipt> Listings { get; set; } = new();
    // Historical JSON only; current auction configuration alone determines relisting.
    public HashSet<uint> WithdrawnTemplates { get; set; } = new();
}
public interface IAuctionListingJournal
{
    Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token);
    Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token);
}
