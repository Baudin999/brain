namespace Brain.Tests;

public sealed class VoicesTests : IDisposable
{
    private const string Transcript = """
        {"type":"user","timestamp":"2026-10-01T09:00:00Z","origin":{"kind":"human"},"message":{"role":"user","content":"Build the brain."}}
        {"type":"assistant","timestamp":"2026-10-01T09:00:01Z","message":{"role":"assistant","content":[{"type":"text","text":"Sure."}]}}
        {"type":"user","timestamp":"2026-10-01T09:00:02Z","toolUseResult":{"stdout":"x"},"message":{"content":[{"type":"tool_result","content":"x"}]}}
        {"type":"user","timestamp":"2026-10-01T09:00:03Z","isMeta":true,"message":{"content":[{"type":"text","text":"Base directory for this skill"}]}}
        {"type":"user","timestamp":"2026-10-01T09:00:04Z","message":{"content":"<command-name>/model</command-name>"}}
        {"type":"user","timestamp":"2026-10-01T09:00:05Z","origin":{"kind":"task-notification"},"message":{"content":"<task-notification>done</task-notification>"}}
        {"type":"user","timestamp":"2026-10-01T09:00:06Z","promptSource":"sdk","message":{"content":"Reply with ok"}}
        {"type":"user","timestamp":"2026-10-01T09:00:07Z","message":{"content":"[Request interrupted by user]"}}
        {"type":"user","timestamp":"2026-10-01T09:00:08Z","origin":{"kind":"human"},"message":{"content":[{"type":"text","text":"Keep <system-reminder>noise</system-reminder>this part."},{"type":"image","source":{}}]}}
        not json at all, "user"
        """;

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void VoicesKeepsOnlyWhatTheHumanTypedAndIsIdempotent()
    {
        var project = Path.Combine(brain.Transcripts, "-home-alice-Projects-x");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "f1d7df00-04e9-4bbc.jsonl"), Transcript);
        File.WriteAllText(Path.Combine(project, "empty-session.jsonl"), "{\"type\":\"assistant\"}\n");

        var first = brain.Ok("voices");
        var second = brain.Ok("voices");

        var file = Path.Combine(brain.Home, "voices", "-home-alice-Projects-x", "2026-10-01-f1d7df00.md");
        Assert.Equal(
            "# -home-alice-Projects-x f1d7df00-04e9-4bbc\n\n## 2026-10-01T09:00:00Z\n\nBuild the brain.\n\n"
            + "## 2026-10-01T09:00:08Z\n\nKeep this part.\n\n",
            File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(Path.Combine(brain.Home, "voices"), "*.md", SearchOption.AllDirectories));
        Assert.StartsWith("voices: 2 transcripts, 1 sessions with typed messages, 2 messages, 1 files written, 0 unchanged", first, StringComparison.Ordinal);
        Assert.StartsWith("voices: 2 transcripts, 1 sessions with typed messages, 2 messages, 0 files written, 1 unchanged", second, StringComparison.Ordinal);
    }

    [Fact]
    public void VoicesRefusesAMissingTranscriptsDirectory()
    {
        Directory.Delete(brain.Transcripts);

        Assert.Equal(1, brain.Run("voices").Code);
    }
}
