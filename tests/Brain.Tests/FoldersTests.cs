using Brain.Code;

namespace Brain.Tests;

/// <summary>The folder a brain code session runs in, as the user types it.</summary>
public sealed class FoldersTests
{
    [Theory]
    [InlineData(".", "/work/here", "/work/here")]
    [InlineData("..", "/work/here", "/work")]
    [InlineData("~/x", "/work/here", "/home/me/x")]
    [InlineData("~", "/work/here", "/home/me")]
    [InlineData("/abs/", "/work/here", "/abs")]
    [InlineData("sub/dir", "/work/here", "/work/here/sub/dir")]
    public void ATypedFolderBecomesAFullPath(string typed, string from, string expected) =>
        Assert.Equal(expected, Folders.Resolve(typed, "/home/me", from));

    [Theory]
    [InlineData("immediate", false)]
    [InlineData("a.b", false)]
    [InlineData(".", true)]
    [InlineData("~/x", true)]
    [InlineData("x/y", true)]
    public void OnlyAPathIsSpelledAsOne(string typed, bool path) =>
        Assert.Equal(path, Folders.LooksLikePath(typed));
}
