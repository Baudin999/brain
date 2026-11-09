using Brain.Search;

namespace Brain.Tests;

/// <summary>
/// The tier a hit gets from its evidence alone: what makes a hit sure, likely or weak, with and without the model. Every
/// cosine is read against the fake model's bars, so nothing here rests on a real model's scale.
/// </summary>
public sealed class EvidenceTests
{
    private static readonly Bars Bars = FakeProfile.Profile.Bars;

    [Fact]
    public void ANameIsSureWhateverElseTheEvidenceSays()
    {
        Assert.Equal(Tier.Sure, new Evidence(Exact: true, Structure: false, Words: 0, Meaning(0)).Tier);
        Assert.Equal(Tier.Sure, new Evidence(Exact: true, Structure: false, Words: 0, Meaning: null).Tier);
    }

    [Fact]
    public void CloseMeaningAloneIsSure()
    {
        Assert.Equal(Tier.Sure, Hit(words: 0, Bars.Close));
        Assert.Equal(Tier.Likely, Hit(words: 0, Bars.Close - 0.01f));
    }

    [Fact]
    public void MostOfTheWordsAreSureOnlyWhenMeaningOrStructureAgrees()
    {
        Assert.Equal(Tier.Sure, Hit(words: Evidence.Most, Bars.Agree));
        Assert.Equal(Tier.Sure, new Evidence(Exact: false, Structure: true, Words: Evidence.Most, Meaning: null).Tier);
        Assert.Equal(Tier.Likely, Hit(words: 1, Bars.Agree - 0.01f));
        Assert.Equal(Tier.Likely, Hit(words: 1, cosine: null));
    }

    [Fact]
    public void MeaningWordsOrStructureAloneAreLikely()
    {
        Assert.Equal(Tier.Likely, Hit(words: 0, Bars.Meant));
        Assert.Equal(Tier.Weak, Hit(words: 0, Bars.Meant - 0.01f));
        Assert.Equal(Tier.Likely, Hit(words: Evidence.Many, cosine: null));
        Assert.Equal(Tier.Likely, new Evidence(Exact: false, Structure: true, Words: 0, Meaning(0)).Tier);
    }

    [Fact]
    public void MeaningAndWordsTooWeakAloneAreLikelyTogether()
    {
        const double words = 0.3;
        var blended = (float)(Bars.Blend - (words / 2));
        Assert.True(blended < Bars.Meant && words < Evidence.Many);

        Assert.Equal(Tier.Likely, Hit(words, blended + 0.01f));
        Assert.Equal(Tier.Weak, Hit(words, blended - 0.01f));
    }

    [Fact]
    public void WordsBelowManyWithoutTheModelAreWeak() =>
        Assert.Equal(Tier.Weak, Hit(words: Evidence.Many - 0.01, cosine: null));

    [Fact]
    public void ACosineIsReadAgainstTheBarsOfTheModelThatGaveIt()
    {
        var low = Bars with { Close = 0.3f, Agree = 0.2f, Meant = 0.1f };

        Assert.Equal(Tier.Weak, new Evidence(Exact: false, Structure: false, Words: 0, new Meaning(0.35f, Bars)).Tier);
        Assert.Equal(Tier.Sure, new Evidence(Exact: false, Structure: false, Words: 0, new Meaning(0.35f, low)).Tier);
    }

    private static Meaning Meaning(float cosine) => new(cosine, Bars);

    private static Tier Hit(double words, float? cosine) =>
        new Evidence(Exact: false, Structure: false, Words: words, cosine is { } value ? Meaning(value) : null).Tier;
}
