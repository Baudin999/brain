using Brain.Voices;

namespace Brain.Commands;

/// <summary>Syncs the user's typed messages from every transcript into voices/, rewriting a file only when it changed.</summary>
internal static class VoicesCommand
{
    public const string Name = "voices";
    private const string TranscriptPattern = "*.jsonl";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Help.UsageOf(Name));
        if (!Directory.Exists(env.Transcripts))
        {
            throw new BrainException($"no transcripts directory at {env.Transcripts}");
        }

        var tally = new VoiceTally();
        var transcripts = Directory.EnumerateDirectories(env.Transcripts)
            .Order(StringComparer.Ordinal)
            .SelectMany(project => Directory.EnumerateFiles(project, TranscriptPattern).Order(StringComparer.Ordinal));
        foreach (var transcript in transcripts)
        {
            Sync(Path.Combine(env.Home, Layout.VoicesDir), transcript, tally);
        }

        env.Out.WriteLine(tally.Summary);
        return ExitCodes.Ok;
    }

    private static void Sync(string voices, string transcript, VoiceTally tally)
    {
        tally.Read();
        var messages = File.ReadLines(transcript).Select(TranscriptFilter.Read).OfType<VoiceMessage>().ToList();
        if (messages.Count == 0)
        {
            return;
        }

        var document = VoiceDocument.For(transcript, messages);
        tally.Kept(document, WriteIfChanged(Path.Combine(voices, document.RelativePath), document.Text));
    }

    private static bool WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return true;
    }
}
