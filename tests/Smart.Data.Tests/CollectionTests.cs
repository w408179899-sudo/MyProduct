using System.Collections.Immutable;
using Smart.Contracts;
using Smart.Data;
using Xunit;
namespace Smart.Data.Tests;

public sealed class CollectionTests
{
    private sealed record Item(int Health, string Name);
    private sealed record Fields(Field<int> Health, Field<string> Name);
    private static readonly CollectionMerger<int, Item, Fields> Merger = new((f, old) =>
    {
        if (old is null && (!f.Health.IsValid || !f.Name.IsValid)) return MergeDecision<Item>.Hold;
        if (!f.Health.IsValid && !f.Name.IsValid) return MergeDecision<Item>.Hold;
        return MergeDecision<Item>.Accept(new(f.Health.IsValid ? f.Health.Value : old!.Health,
            f.Name.IsValid ? f.Name.Value : old!.Name));
    });
    private static PublishedSnapshot<ImmutableDictionary<int, Item>> Previous() =>
        new(ImmutableDictionary<int, Item>.Empty.Add(1, new(10, "first")).Add(2, new(20, "second")), new(1, 1), DateTimeOffset.UtcNow);
    [Fact] public void PartialMergesGoodFieldsAndRetainsOmittedEntities()
    {
        var result = Merger.Merge(ReadCompleteness.Partial,
            [new(1, new(Field<int>.Valid(0), Field<string>.Missing))], Previous());
        Assert.True(result.Publish); Assert.Equal(0, result.Value[1].Health);
        Assert.Equal("first", result.Value[1].Name); Assert.Equal(20, result.Value[2].Health);
    }
    [Fact] public void CompleteTraversalPrunesProvenAbsence()
    {
        var result = Merger.Merge(ReadCompleteness.Complete,
            [new(1, new(Field<int>.Valid(5), Field<string>.Valid("new")))], Previous());
        Assert.Single(result.Value); Assert.Equal("new", result.Value[1].Name);
    }
    [Fact] public void CompleteEmptyClearsButPartialEmptyHolds()
    {
        Assert.Empty(Merger.Merge(ReadCompleteness.Complete, [], Previous()).Value);
        Assert.False(Merger.Merge(ReadCompleteness.Partial, [], Previous()).Publish);
        Assert.False(Merger.Merge(ReadCompleteness.Failed, [], Previous()).Publish);
    }
    [Fact] public void IdentityMismatchAndDuplicateIdentityCannotMerge()
    {
        var fields = new Fields(Field<int>.Valid(1), Field<string>.Valid("x"));
        Assert.False(Merger.Merge(ReadCompleteness.Complete, [new(1, fields, false)], Previous()).Publish);
        Assert.False(Merger.Merge(ReadCompleteness.Complete, [new(1, fields), new(1, fields)], Previous()).Publish);
    }
    [Fact] public void ColdPartialWithMissingRequiredFieldsCannotInventAnEntity()
    {
        Assert.False(Merger.Merge(ReadCompleteness.Partial, [new(3, new(Field<int>.Valid(9), Field<string>.Missing))], null).Publish);
    }
    [Fact] public void PartialCanPublishACompleteNewEntityImmediately()
    {
        var result = Merger.Merge(ReadCompleteness.Partial,
            [new(3, new(Field<int>.Valid(9), Field<string>.Valid("third")))], Previous());
        Assert.True(result.Publish); Assert.Equal(3, result.Value.Count);
    }
    [Theory] [InlineData(ReadCompleteness.Partial)] [InlineData(ReadCompleteness.Complete)]
    public void InvalidObjectDoesNotSuppressIndependentUpdatesOrPrune(ReadCompleteness completeness)
    {
        var result = Merger.Merge(completeness,
            [new(1, new(Field<int>.Valid(0), Field<string>.Missing)),
             new(99, new(Field<int>.Valid(50), Field<string>.Valid("invalid")), false)], Previous());
        Assert.True(result.Publish); Assert.Equal(0, result.Value[1].Health);
        Assert.Equal(20, result.Value[2].Health); Assert.False(result.Value.ContainsKey(99));
    }
    [Fact] public void AllDuplicateObservationsAreExcludedButUnrelatedObjectsAdvance()
    {
        var fields = new Fields(Field<int>.Valid(99), Field<string>.Valid("duplicate"));
        var result = Merger.Merge(ReadCompleteness.Partial,
            [new(1, fields), new(2, fields), new(1, fields)], Previous());
        Assert.True(result.Publish); Assert.Equal(10, result.Value[1].Health);
        Assert.Equal(99, result.Value[2].Health);
    }
}
