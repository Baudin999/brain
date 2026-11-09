using System.Text;
using System.Text.Json;
using static Brain.Children.ClaudeStream;

namespace Brain.Code;

/// <summary>
/// The text the session's own agent is writing now, as claude streams it: each partial event of its top level, a
/// <c>stream_event</c> line, starts a message, starts a text block or adds to it. The text is known by the id of its
/// message, which <c>message_start</c> names as the Anthropic stream does and the transcript's line of it holds, so the
/// window shows it only until the transcript holds that message's text; without an id there is no live text. It ends
/// with the turn, since a text an interrupt abandoned may never reach the transcript. Claude Code streams no subagent's
/// text, so a subagent's view reads from its transcript only.
/// </summary>
internal sealed class LiveText
{
    private readonly Lock gate = new();
    private readonly StringBuilder text = new();
    private string? message;

    /// <summary>One partial event of the session's own agent, the whole line.</summary>
    public void Streamed(JsonElement line)
    {
        if (!line.TryGetProperty("event", out var streamed))
        {
            return;
        }

        lock (gate)
        {
            switch (Text(streamed, "type"))
            {
                case "message_start":
                    Started(streamed);
                    break;
                case "content_block_start":
                    text.Clear();
                    break;
                case "content_block_delta":
                    Added(streamed);
                    break;
            }
        }
    }

    /// <summary>The turn ended: what was written is the transcript's, or was abandoned.</summary>
    public void End()
    {
        lock (gate)
        {
            message = null;
            text.Clear();
        }
    }

    /// <summary>What is written so far, with the id of its message; null before any text came, and without an id.</summary>
    public CodeLive? Now()
    {
        lock (gate)
        {
            var written = text.ToString().Trim();
            if (string.IsNullOrEmpty(message) || written.Length == 0)
            {
                return null;
            }

            return new CodeLive(message, written);
        }
    }

    /// <summary>A message starts, under the gate: its id, from the message the event carries.</summary>
    private void Started(JsonElement streamed)
    {
        text.Clear();
        message = null;
        if (streamed.TryGetProperty("message", out var started))
        {
            message = Text(started, "id");
        }
    }

    /// <summary>Text comes in, under the gate; any other delta, as thinking, is no text.</summary>
    private void Added(JsonElement streamed)
    {
        if (!streamed.TryGetProperty("delta", out var delta))
        {
            return;
        }

        if (Text(delta, "type") != "text_delta")
        {
            return;
        }

        text.Append(Text(delta, "text"));
    }
}

/// <summary>The text the agent is writing now, and the id of the message it is part of.</summary>
internal sealed record CodeLive(string Message, string Text);
