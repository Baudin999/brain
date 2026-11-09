using System.Text;

namespace Brain.Voices;

/// <summary>The voices file of one session: <c>voices/&lt;project&gt;/&lt;date&gt;-&lt;session8&gt;.md</c>.</summary>
internal sealed record VoiceDocument(string Project, string SessionId, IReadOnlyList<VoiceMessage> Messages)
{
    private const int SessionPrefix = 8;
    private const int DateLength = 10;
    private const string Undated = "undated";
    private const string Dash = "-";
    private const string Heading = "## ";
    private const string Title = "# ";
    private const char Space = ' ';
    private const string Paragraph = "\n\n";

    public static VoiceDocument For(string transcript, IReadOnlyList<VoiceMessage> messages) =>
        new(Path.GetFileName(Path.GetDirectoryName(transcript)) ?? string.Empty, Path.GetFileNameWithoutExtension(transcript), messages);

    public string RelativePath => Path.Combine(Project, Date + Dash + SessionId[..Math.Min(SessionPrefix, SessionId.Length)] + Layout.NodeExtension);

    public string Text
    {
        get
        {
            var text = new StringBuilder().Append(Title).Append(Project).Append(Space).Append(SessionId).Append(Paragraph);
            foreach (var message in Messages)
            {
                text.Append(Heading).Append(message.Timestamp).Append(Paragraph).Append(message.Text).Append(Paragraph);
            }

            return text.ToString();
        }
    }

    private string Date
    {
        get
        {
            var stamp = Messages[0].Timestamp;
            return stamp.Length >= DateLength ? stamp[..DateLength] : Undated;
        }
    }
}
