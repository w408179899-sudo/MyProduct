using System.Collections.Immutable;
using Smart.Contracts;
namespace Smart.Data;

public sealed record EntityObservation<TKey, TFields>(TKey Identity, TFields Fields, bool IdentityValid = true)
    where TKey : notnull;

public sealed class CollectionMerger<TKey, TItem, TFields>(
    Func<TFields, TItem?, MergeDecision<TItem>> mergeFields) :
    ISnapshotMerger<ImmutableDictionary<TKey, TItem>, ImmutableArray<EntityObservation<TKey, TFields>>>
    where TKey : notnull where TItem : class
{
    public MergeDecision<ImmutableDictionary<TKey, TItem>> Merge(ReadCompleteness completeness,
        ImmutableArray<EntityObservation<TKey, TFields>> observed,
        PublishedSnapshot<ImmutableDictionary<TKey, TItem>>? previous)
    {
        if (completeness == ReadCompleteness.Failed || observed.IsDefault)
            return MergeDecision<ImmutableDictionary<TKey, TItem>>.Hold;
        var old = previous?.Value ?? ImmutableDictionary<TKey, TItem>.Empty;
        var builder = old.ToBuilder();
        var seen = new HashSet<TKey>();
        var ambiguous = new HashSet<TKey>();
        foreach (var item in observed)
            if (!item.IdentityValid || !seen.Add(item.Identity)) ambiguous.Add(item.Identity);
        var updates = 0;
        var canPrune = completeness == ReadCompleteness.Complete && ambiguous.Count == 0;
        foreach (var item in observed)
        {
            // Reject every observation of an ambiguous identity, including the first duplicate.
            // Independent valid entities can still advance; ambiguous traversal never prunes.
            if (ambiguous.Contains(item.Identity)) continue;
            old.TryGetValue(item.Identity, out var last);
            var merged = mergeFields(item.Fields, last);
            if (merged.Publish) { builder[item.Identity] = merged.Value; updates++; }
            else if (last is null) canPrune = false;
        }
        if (canPrune)
        {
            foreach (var key in old.Keys)
                if (!seen.Contains(key)) builder.Remove(key);
        }
        if (updates == 0 && !canPrune) return MergeDecision<ImmutableDictionary<TKey, TItem>>.Hold;
        return MergeDecision<ImmutableDictionary<TKey, TItem>>.Accept(builder.ToImmutable());
    }
}
