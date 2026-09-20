using Roadhog.Core.Model;

namespace Roadhog.Application.Trading;

public sealed record AuctionListingReceipt(uint ListingId, uint TemplateId, ulong UnitPrice, DateTimeOffset ListedAt);
public sealed class AuctionListingHistory
{
    public List<AuctionListingReceipt> Listings { get; set; } = new();
    public HashSet<uint> WithdrawnTemplates { get; set; } = new();
    public bool IsOld(AuctionListing listing, DateTimeOffset now, int hours) => Listings.Any(r =>
        r.ListingId == listing.ListingId && r.TemplateId == listing.TemplateId && listing.Quantity > 0 &&
        listing.TotalPrice / listing.Quantity == r.UnitPrice && now >= r.ListedAt.AddHours(hours) && now <= r.ListedAt.AddDays(8));
}
public interface IAuctionListingJournal
{
    Task<AuctionListingHistory> LoadAsync(string account, string character, CancellationToken token);
    Task SaveAsync(string account, string character, AuctionListingHistory history, CancellationToken token);
}
