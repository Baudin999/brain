using Brain.Migrating;

namespace Brain.Tests;

/// <summary>
/// brain migrate: the old one-line label split into title and why, and the old model moved to information and idea,
/// mechanically: title, body and why stay as written.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    private const string OldConcept =
        "# meadow\n\nWe cover open ground, because bare ground read as unfinished\n\n- Kind: decision\n- Phase: concept\n- Named-by: user\n"
        + "- Born: 2026-10-01\n- Session: f1d7\n- Superseded-by: meadow-v2\n\n## Edges\n- built -> commit:b09256f9\n- part-of -> wg-12\n\n## Notes\n\nGrass first.\n\nThen moss.\n";

    private const string OldIdea = "# wg-12\n\nThe ground phase\n\n- Kind: decision\n- Phase: idea\n- Named-by: claude\n- Born: 2026-10-02\n\n## Edges\n";

    private const string OldWhyless = "# bare\n\nA concept that never said why\n\n- Kind: decision\n- Phase: concept\n- Named-by: user\n- Born: 2026-10-02\n\n## Edges\n";

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string Global(string slug) => Path.Combine(brain.Home, "graph", "global", slug + ".md");

    [Theory]
    [InlineData("We do Y, because X holds", "We do Y", "X holds.")]
    [InlineData("Ground first because the eye reads it first.", "Ground first", "The eye reads it first.")]
    [InlineData("Ask why, because otherwise? Nobody knows!", "Ask why", "Otherwise? Nobody knows!")]
    [InlineData("One because, two because three", "One because, two", "Three.")]
    [InlineData("A label without a reason", "A label without a reason", "")]
    [InlineData("Because at the start is not a split", "Because at the start is not a split", "")]
    public void TheLabelSplitsAtItsFirstBecause(string label, string title, string why) =>
        Assert.Equal((title, why), LabelSplit.Split(label));

    [Fact]
    public void MigrateWritesTheNewFormatAndTheNewModelAndKeepsNotesAndOtherEdges()
    {
        File.WriteAllText(Global("meadow"), OldConcept);
        File.WriteAllText(Global("wg-12"), OldIdea);

        Assert.Equal("migrate: migrated 2 nodes, 1 with a why and 1 without; 0 moved to the new model; 0 already in the new format\n", brain.Ok("migrate"));

        Assert.Equal(
            "---\nkind: information\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\nsession: f1d7\n---\n"
            + "# We cover open ground\n\nGrass first.\n\nThen moss.\n\n## Why\nBare ground read as unfinished.\n\n"
            + "## Edges\n- part-of -> wg-12\n",
            File.ReadAllText(Global("meadow")));
        Assert.Equal(
            "---\nkind: idea\nsubjects: []\nnamed-by: claude\nborn: 2026-10-02\n---\n# The ground phase\n\n## Edges\n",
            File.ReadAllText(Global("wg-12")));
        brain.Ok("check");
    }

    [Fact]
    public void MigrateMovesTheFourKindsToInformationAndIdeaAndDropsPhaseSupersededByAndCommits()
    {
        brain.WriteNode("global", "built", "kind: decision\nphase: concrete\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Ground first", "- built -> commit:b0e944e\n- part-of -> wish\n");
        brain.WriteNode("global", "old", "kind: decision\nphase: concept\nsuperseded-by: built\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Ground second");
        brain.WriteNode("global", "wish", "kind: decision\nphase: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Ground everywhere");
        brain.WriteNode("global", "unphased", "kind: decision\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Ground once");
        brain.WriteNode("global", "ollama", "kind: tool\nsubjects: []\nnamed-by: agent\nborn: 2026-10-01\n", "Ollama serves embeddings");
        brain.WriteNode("personal", "user", "kind: person\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "The user");
        brain.WriteNode("global", "goal", "kind: goal\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Worlds people want");

        var dry = brain.Ok("migrate", "--dry-run");
        var output = brain.Ok("migrate");

        Assert.Contains("global:built: decision concrete -> information\n", dry, StringComparison.Ordinal);
        Assert.Contains("global:wish: decision idea -> idea\n", dry, StringComparison.Ordinal);
        Assert.Equal("migrate: migrated 0 nodes, 0 with a why and 0 without; 7 moved to the new model; 0 already in the new format\n", output);
        Assert.Equal("---\nkind: information\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n---\n# Ground first\n\n## Edges\n- part-of -> wish\n", File.ReadAllText(Global("built")));
        Assert.Equal("---\nkind: information\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n---\n# Ground second\n\n## Edges\n", File.ReadAllText(Global("old")));
        Assert.StartsWith("---\nkind: idea\nsubjects: []\n", File.ReadAllText(Global("wish")), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: []\n", File.ReadAllText(Global("unphased")), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: []\n", File.ReadAllText(Global("ollama")), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: []\n", File.ReadAllText(Global("goal")), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: []\n", brain.NodeText("personal", "user"), StringComparison.Ordinal);
        var (code, _, errors) = brain.Run("check");
        Assert.Equal(0, code);
        Assert.StartsWith("migration: 6 information nodes have no why: ", errors, StringComparison.Ordinal);
        Assert.Equal("migrate: migrated 0 nodes, 0 with a why and 0 without; 0 moved to the new model; 7 already in the new format\n", brain.Ok("migrate"));
    }

    [Fact]
    public void MigrateMovesKindsFromBeforeTheFourAndCheckFailsTheOnesLeft()
    {
        brain.WriteNode("global", "lesson", "kind: lesson\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "Ground reads first");
        brain.WriteNode("global", "stage", "kind: phase\nphase: idea\nsubjects: []\nnamed-by: agent\nborn: 2026-10-01\n", "The ground stage");
        brain.WriteNode("global", "belief", "kind: belief\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "An old belief");

        var before = brain.Run("check");
        var dry = brain.Ok("migrate", "--dry-run");
        var output = brain.Ok("migrate");
        var after = brain.Run("check");

        Assert.Equal(1, before.Code);
        Assert.Contains("global:lesson: kind lesson is from the old model; brain migrate moves it", before.Err, StringComparison.Ordinal);
        Assert.Contains("global:lesson: lesson -> idea\nglobal:stage: phase idea -> information\n", dry, StringComparison.Ordinal);
        Assert.Equal("migrate: migrated 0 nodes, 0 with a why and 0 without; 2 moved to the new model; 1 already in the new format\n", output);
        Assert.StartsWith("---\nkind: idea\nsubjects: []\n", File.ReadAllText(Global("lesson")), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: []\n", File.ReadAllText(Global("stage")), StringComparison.Ordinal);
        Assert.Equal(1, after.Code);
        Assert.Equal("global:belief: kind belief is not one of information, idea\nmigration: 1 information nodes have no why: global:stage\n", after.Err);
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        File.WriteAllText(Global("meadow"), OldConcept);
        brain.Ok("migrate");
        var once = File.ReadAllText(Global("meadow"));

        Assert.Equal("migrate: migrated 0 nodes, 0 with a why and 0 without; 0 moved to the new model; 1 already in the new format\n", brain.Ok("migrate"));
        Assert.Equal(once, File.ReadAllText(Global("meadow")));
    }

    [Fact]
    public void DryRunPrintsEveryNodeAndWritesNothing()
    {
        File.WriteAllText(Global("meadow"), OldConcept);

        var output = brain.Ok("migrate", "--dry-run");

        Assert.Equal(
            "global:meadow\n  label: We cover open ground, because bare ground read as unfinished\n  title: We cover open ground\n"
            + "  why:   Bare ground read as unfinished.\nmigrate: would migrate 1 nodes, 1 with a why and 0 without; 0 moved to the new model; 0 already in the new format\n",
            output);
        Assert.Equal(OldConcept, File.ReadAllText(Global("meadow")));
    }

    [Fact]
    public void AConceptWithoutBecauseMigratesAndCheckListsItWithoutFailing()
    {
        File.WriteAllText(Global("bare"), OldWhyless);

        brain.Ok("migrate");
        var (code, output, errors) = brain.Run("check");

        Assert.Contains("# A concept that never said why\n\n## Edges\n", File.ReadAllText(Global("bare")), StringComparison.Ordinal);
        Assert.Equal(0, code);
        Assert.Equal("check: 1 nodes, 0 edges, 0 errors in global, personal\n", output);
        Assert.Equal("migration: 1 information nodes have no why: global:bare\n", errors);
        Assert.Contains("needs a why", brain.Run("update", "bare", "A concept that says it").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteToAnOldFileIsRefusedSoItIsNeverEmptied()
    {
        File.WriteAllText(Global("meadow"), OldConcept);
        brain.Ok("add", "--subjects", "test", "global", "ground", "Ground", "--kind", "idea");

        var (code, _, errors) = brain.Run("link", "meadow", "see", "ground");

        Assert.Equal(1, code);
        Assert.Equal("brain: global:meadow is in the old format; run brain migrate before writing it\n", errors);
        Assert.Equal(OldConcept, File.ReadAllText(Global("meadow")));
    }

    [Fact]
    public void AnOldFileWithALineItCannotPlaceRefusesTheWholeRun()
    {
        File.WriteAllText(Global("meadow"), OldConcept);
        File.WriteAllText(Global("odd"), "# odd\n\nA label\n\nstray prose\n\n- Kind: tool\n- Named-by: user\n- Born: 2026-10-02\n\n## Edges\n");

        var (code, _, errors) = brain.Run("migrate");

        Assert.Equal(1, code);
        Assert.Contains("odd: cannot migrate", errors, StringComparison.Ordinal);
        Assert.Equal(OldConcept, File.ReadAllText(Global("meadow")));
    }
}
