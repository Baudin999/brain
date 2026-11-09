using System.Text;

namespace Brain.Voices;

/// <summary>What one voices sync did: transcripts read, sessions and messages kept, files written and their bytes.</summary>
internal sealed class VoiceTally
{
    private readonly List<(int Messages, long Bytes, bool Written)> kept = [];
    private int transcripts;

    public string Summary
    {
        get
        {
            var written = kept.Count(session => session.Written);
            return $"voices: {transcripts} transcripts, {kept.Count} sessions with typed messages, "
                + $"{kept.Sum(session => session.Messages)} messages, {written} files written, {kept.Count - written} unchanged, "
                + $"{kept.Sum(session => session.Bytes)} bytes";
        }
    }

    public void Read() => transcripts++;

    public void Kept(VoiceDocument document, bool wasWritten) =>
        kept.Add((document.Messages.Count, Encoding.UTF8.GetByteCount(document.Text), wasWritten));
}
