using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brain.Questions;

/// <summary>
/// The user's answers to the sources' questions, in the data repo's gitignored <c>cache/answers</c>: one file per answer,
/// <c>&lt;source&gt;/&lt;question id&gt;.json</c>, written whole through a rename. Only brain serve writes here, on the
/// user's action in the window, and the graph door guards the directory like the pending writes. Brain never deletes
/// an answer, and never writes a second one over it: the source collects it on its next run.
/// </summary>
internal sealed class AnswerStore(string home)
{
    private const string AnswersDir = "answers";
    private const string JsonExtension = ".json";

    public string Root { get; } = Path.Combine(home, Layout.CacheDir, AnswersDir);

    public bool Has(string source, string id) => File.Exists(PathOf(source, id));

    public SourceAnswer Write(string source, SourceAnswer answer)
    {
        var path = PathOf(source, answer.Id);
        BrainException.ThrowIf(File.Exists(path) ? $"{source} question {answer.Id} has an answer already" : null);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.Serialize(answer, AnswerJson.Default.SourceAnswer));
        return answer;
    }

    private string PathOf(string source, string id)
    {
        BrainException.ThrowIf(Sources.IsName(source) && Sources.IsName(id) ? null : $"{source} {id} names no answer file");
        return Path.Combine(Root, source, id + JsonExtension);
    }
}

/// <summary>The user's answer to one question: the choice's id, their words if any, and when they gave it.</summary>
internal sealed record SourceAnswer(string Id, string Choice, string? Words, DateTimeOffset AnsweredAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceAnswer))]
internal sealed partial class AnswerJson : JsonSerializerContext;
