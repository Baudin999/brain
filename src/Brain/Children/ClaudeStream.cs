using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Brain.Children;

/// <summary>
/// How brain talks with a <c>claude -p</c> that reads and writes stream-json, as brain serve's open agent and brain
/// code's sessions do: the start of its process with its input and output redirected as UTF-8, the user's words as one
/// line of input, and the string fields of what it says.
/// </summary>
internal static class ClaudeStream
{
    /// <summary>The flags that make claude read the user's words and say its events as stream-json, one JSON object a line.</summary>
    public static readonly IReadOnlyList<string> Flags = ["--input-format", "stream-json", "--output-format", "stream-json", "--verbose"];

    /// <summary>
    /// How to start <paramref name="program"/>, <c>claude</c> or a test's stand-in, with <paramref name="arguments"/> in
    /// <paramref name="cwd"/>, its input, output and errors redirected.
    /// </summary>
    public static ProcessStartInfo Info(string program, string cwd, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            WorkingDirectory = cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>The user's words as one stream-json line.</summary>
    public static string UserMessage(string words) => UserMessage(words, []);

    /// <summary>
    /// The user's words with the images he attached as one stream-json line: each image an image content block, as a
    /// paste in a terminal sends it, ahead of his words. Words without images are the content as a string.
    /// </summary>
    public static string UserMessage(string words, IReadOnlyList<ImageBlock> images)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "user");
            json.WriteStartObject("message");
            json.WriteString("role", "user");
            if (images.Count == 0)
            {
                json.WriteString("content", words);
            }
            else
            {
                WriteBlocks(json, words, images);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The content of a message with images: an image block for each, then the words as a text block, unless there are none.</summary>
    private static void WriteBlocks(Utf8JsonWriter json, string words, IReadOnlyList<ImageBlock> images)
    {
        json.WriteStartArray("content");
        foreach (var image in images)
        {
            json.WriteStartObject();
            json.WriteString("type", "image");
            json.WriteStartObject("source");
            json.WriteString("type", "base64");
            json.WriteString("media_type", image.MediaType);
            json.WriteString("data", image.Data);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        if (words.Length > 0)
        {
            json.WriteStartObject();
            json.WriteString("type", "text");
            json.WriteString("text", words);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// The control request <paramref name="request"/> that ends claude's turn, as Ctrl+C does in a terminal, as one
    /// stream-json line. claude answers it with a control response, ends the turn with an error result, and reads the next
    /// words. A SIGINT would end claude itself.
    /// </summary>
    public static string InterruptRequest(string request)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "control_request");
            json.WriteString("request_id", request);
            json.WriteStartObject("request");
            json.WriteString("subtype", "interrupt");
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The string field <paramref name="name"/> of <paramref name="element"/>, or empty when it has none.</summary>
    public static string Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString()!;
    }
}

/// <summary>An image as a message carries it: its media type, as <c>image/png</c>, and its bytes in base64.</summary>
internal sealed record ImageBlock(string MediaType, string Data);
