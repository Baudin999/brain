using Brain.Commands;

namespace Brain.Tests;

/// <summary>
/// How <c>brain code</c> reads its positionals: one that names an existing folder is the folder and any other is the
/// agent, in either order; a path that is no folder, two folders and two agents are refused.
/// </summary>
public sealed class CodeCommandTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly string work;
    private readonly string other;
    private readonly BrainEnv env;

    public CodeCommandTests()
    {
        work = Path.Combine(brain.Root, "work");
        other = Path.Combine(brain.Root, "other");
        Directory.CreateDirectory(Path.Combine(work, "sub"));
        Directory.CreateDirectory(other);
        env = brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null) with { Cwd = work };
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void NoPositionalIsNeitherAgentNorFolder() =>
        Assert.Equal((null, null), CodeCommand.Positionals(env, []));

    [Theory]
    [InlineData(".")]
    [InlineData("sub/..")]
    public void AFolderIsResolvedAgainstTheDirectoryTheCommandRunsFrom(string typed) =>
        Assert.Equal((null, work), CodeCommand.Positionals(env, [typed]));

    [Fact]
    public void AnAgentAndAFolderGoInEitherOrder()
    {
        Assert.Equal(("immediate", other), CodeCommand.Positionals(env, ["immediate", other]));
        Assert.Equal(("immediate", other), CodeCommand.Positionals(env, [other, "immediate"]));
    }

    [Fact]
    public void ATildeIsTheUsersHome() =>
        Assert.Equal(("immediate", other), CodeCommand.Positionals(env, ["immediate", "~/other"]));

    [Fact]
    public void APathThatIsNoFolderIsRefusedByName()
    {
        var refused = Assert.Throws<BrainException>(() => CodeCommand.Positionals(env, ["./nowhere"]));

        Assert.Contains(Path.Combine(work, "nowhere"), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoFoldersAreRefused() =>
        Assert.Throws<BrainException>(() => CodeCommand.Positionals(env, [".", other]));

    [Fact]
    public void TwoAgentsAreRefused() =>
        Assert.Throws<BrainException>(() => CodeCommand.Positionals(env, ["brain", "immediate"]));
}
