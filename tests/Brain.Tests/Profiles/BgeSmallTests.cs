using Brain.Search;
using Brain.Search.Profiles;

namespace Brain.Tests.Profiles;

/// <summary>What bge-small-en-v1.5's bars make of the evidence measured with it on the search test set.</summary>
public sealed class BgeSmallTests
{
    [Fact]
    public void AWantedNodeWhoseMeaningAndWordsAreTooLowAloneIsLikely()
    {
        Assert.Equal(Tier.Likely, Hit(words: 0.3, cosine: 0.58f));
        Assert.Equal(Tier.Weak, Hit(words: 0.3, cosine: 0.53f));
    }

    /// <summary>
    /// The most the nonsense queries of the search test set reached in their top hits on the user's graph: a cosine of
    /// 0.49 and a word share of 0.33.
    /// </summary>
    [Fact]
    public void WhatNonsenseReachesIsWeak() => Assert.Equal(Tier.Weak, Hit(words: 0.33, cosine: 0.49f));

    private static Tier Hit(double words, float cosine) =>
        new Evidence(Exact: false, Structure: false, words, new Meaning(cosine, BgeSmall.Profile.Bars)).Tier;
}
