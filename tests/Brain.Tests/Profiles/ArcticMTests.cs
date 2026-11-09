using Brain.Search;
using Brain.Search.Profiles;

namespace Brain.Tests.Profiles;

/// <summary>What snowflake-arctic-embed-m-v2.0's bars make of the evidence measured with it on the search test set.</summary>
public sealed class ArcticMTests
{
    /// <summary>
    /// The most the nonsense queries of the search test set reached in their hits on the user's graph: a cosine of 0.20
    /// and a word share of 0.32, and together a blend of 0.34.
    /// </summary>
    [Fact]
    public void WhatNonsenseReachesIsWeak()
    {
        Assert.Equal(Tier.Weak, Hit(words: 0.32, cosine: 0.20f));
    }

    /// <summary>The highest cosine of a node a query did not want, 0.525, is not sure on its own.</summary>
    [Fact]
    public void TheNearestUnwantedNodeIsNotSure() => Assert.NotEqual(Tier.Sure, Hit(words: 0, cosine: 0.525f));

    /// <summary>
    /// Of the hits with most of the query's words, the unwanted ones reached a cosine of 0.25, and the wanted ones 0.30
    /// and up, but for one at 0.26.
    /// </summary>
    [Fact]
    public void MostOfTheWordsAreSureOnlyWithAWantedNodesMeaning()
    {
        Assert.Equal(Tier.Sure, Hit(words: 0.8, cosine: 0.30f));
        Assert.NotEqual(Tier.Sure, Hit(words: 0.8, cosine: 0.25f));
    }

    private static Tier Hit(double words, float cosine) =>
        new Evidence(Exact: false, Structure: false, words, new Meaning(cosine, ArcticM.Profile.Bars)).Tier;
}
