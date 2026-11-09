using Brain.Search;

namespace Brain.Tests.Profiles;

/// <summary>The models brain ships: each one in a directory of its own, and the active one the one the settings name.</summary>
public sealed class EmbedderProfilesTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void WithoutASettingTheDefaultModelRunsAndItIsOneBrainShips()
    {
        Assert.Contains(EmbedderProfiles.Default, EmbedderProfiles.All);
        Assert.Same(EmbedderProfiles.Default, EmbedderProfiles.Active(brain.Home));
    }

    [Fact]
    public void TheModelTheSettingsNameRuns()
    {
        var other = EmbedderProfiles.All.Single(profile => profile != EmbedderProfiles.Default);
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), $$"""{"{{BrainSettings.EmbedderKey}}":"{{other.Name}}"}""");

        Assert.Same(other, EmbedderProfiles.Active(brain.Home));
    }

    [Fact]
    public void ASettingThatNamesNoModelBrainShipsIsRefused()
    {
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), $$"""{"{{BrainSettings.EmbedderKey}}":"no-such-model"}""");

        var refusal = Assert.Throws<BrainException>(() => EmbedderProfiles.Active(brain.Home));
        Assert.Contains("embedder is \"no-such-model\", not one of " + EmbedderProfiles.Default.Name, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryModelHasItsOwnNameAndFiles()
    {
        Assert.Equal(EmbedderProfiles.All.Count, EmbedderProfiles.All.Select(profile => profile.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(EmbedderProfiles.All, profile => Assert.Equal(profile.Files.Count, profile.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count()));
    }
}
