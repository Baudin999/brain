using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brain.Questions;

/// <summary>
/// What a source prints: its name and its open questions. Brain reads the shape and none of the meaning: a caption, a
/// tag and a choice's label are the source's text, which the window shows as it is.
/// </summary>
internal sealed record SourceReply(string Source, IReadOnlyList<Question> Questions)
{
    /// <summary>Reads and checks a source's output; throws a refusal that names the first thing wrong with it.</summary>
    public static SourceReply Parse(string name, string json)
    {
        SourceReply? reply;
        try
        {
            reply = JsonSerializer.Deserialize(json, QuestionJson.Default.SourceReply);
        }
        catch (JsonException error)
        {
            throw new BrainException($"it printed invalid JSON: {error.Message}");
        }

        reply = reply ?? throw new BrainException("it printed null");
        BrainException.ThrowIf(reply.Source == name ? null : $"it says it is \"{reply.Source}\", and {Sources.FileName} names it {name}");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in reply.Questions)
        {
            BrainException.ThrowIf(question.Problem() is { } problem ? $"question \"{question.Id}\": {problem}" : null);
            BrainException.ThrowIf(ids.Add(question.Id) ? null : $"it lists question \"{question.Id}\" twice");
        }

        return reply;
    }
}

/// <summary>
/// One question for the user: the repo it is about as an absolute path, a title, the source's tags, when it was asked,
/// the blocks the window renders in order, and the choices the user picks one of. The rest is optional: the group the
/// inbox lists it under, the commit it is about, whether the source says that commit is older code, and the query its
/// context opens on, which the source's context command answers. brain compares no commit: stale is the source's word.
/// </summary>
internal sealed record Question(
    string Id,
    string Repo,
    string Title,
    DateTimeOffset Created,
    IReadOnlyList<QuestionBlock> Blocks,
    IReadOnlyList<QuestionChoice> Choices,
    IReadOnlyList<string>? Tags = null,
    string? Group = null,
    string? Commit = null,
    bool Stale = false,
    string? Context = null,
    IReadOnlyList<FocusLines>? Focus = null)
{
    public string? Problem()
    {
        if (!Sources.IsName(Id))
        {
            return "an id is letters, digits, dot, dash and underscore";
        }

        if (Choices.Count == 0)
        {
            return "it offers no choice";
        }

        if (Choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != Choices.Count)
        {
            return "two choices share an id";
        }

        return Focus?.Select(focus => focus.Problem()).FirstOrDefault(problem => problem is not null)
            ?? Blocks.Select((block, index) => block.Problem() is { } problem ? $"block {index + 1}, {block.Type}: {problem}" : null).FirstOrDefault(problem => problem is not null);
    }
}

/// <summary>
/// Lines a question asks every query of its context to show: a repo-relative path and the file's line numbers, any
/// number of them. brain matches a context's code block on its path and line numbers alone.
/// </summary>
internal sealed record FocusLines(string Path, IReadOnlyList<int> Lines)
{
    public string? Problem() =>
        string.IsNullOrWhiteSpace(Path) ? "a focus has no path"
        : Lines.Any(line => line < 1) ? $"focus \"{Path}\" has a line below 1"
        : null;

    /// <summary>The focus lines among a file's lines first to last, inclusive; none for another path.</summary>
    public IEnumerable<int> Within(string? path, int first, int last) => path == Path ? Lines.Where(line => line >= first && line <= last) : [];
}

internal sealed record QuestionChoice(string Id, string Label);

/// <summary>
/// One block of a question, of a type the window knows: text, code, diff, claims, facts or timeline. A block of another
/// type keeps its fields in <see cref="Rest"/>, and the window shows it as a block it does not know, never dropping it.
/// A timeline's steps hold ordinary blocks, never another timeline.
/// </summary>
internal sealed record QuestionBlock
{
    /// <summary>The block with the focus lines it shows added to its highlights; the same block when none show.</summary>
    public QuestionBlock Focused(IReadOnlyList<FocusLines> focus)
    {
        if (Type != CodeType || Code is null)
        {
            return this;
        }

        var span = new Drift.CodeSpan(Code, StartLine ?? 1);
        var lines = focus.SelectMany(one => one.Within(Path, span.First, span.Last)).ToList();
        return lines.Count == 0 ? this : this with { Highlights = [.. (Highlights ?? []).Union(lines).Order()] };
    }

    public const string TextType = "text";
    public const string CodeType = "code";
    public const string DiffType = "diff";
    public const string ClaimsType = "claims";
    public const string FactsType = "facts";
    public const string TimelineType = "timeline";

    public required string Type { get; init; }

    public string? Caption { get; init; }

    public string? Text { get; init; }

    public string? Path { get; init; }

    public int? StartLine { get; init; }

    public string? Code { get; init; }

    public IReadOnlyList<int>? Highlights { get; init; }

    public IReadOnlyList<CodeMark>? Marks { get; init; }

    public Tally? Tally { get; init; }

    public IReadOnlyList<DiffFile>? Files { get; init; }

    public IReadOnlyList<Claim>? Claims { get; init; }

    public IReadOnlyList<Fact>? Facts { get; init; }

    /// <summary>On a facts block: true asks brain to show whether the facts' values are equal.</summary>
    public bool? Agreement { get; init; }

    public IReadOnlyList<TimelineStep>? Steps { get; init; }

    /// <summary>
    /// The fields of a block brain does not know, kept so the window can show them. A setter, not init: the source
    /// generator binds an init property as a constructor member, which extension data refuses.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }

    public string? Problem() => Problem(inTimeline: false);

    private string? Problem(bool inTimeline) => Type switch
    {
        TextType when Text is null => "it has no text",
        CodeType when Code is null => "it has no code",
        DiffType when Files is not { Count: > 0 } => "it has no files",
        ClaimsType when Claims is null => "it has no claims",
        FactsType when Facts is null => "it has no facts",
        FactsType => Facts.Select(fact => Tones.Problem(fact.Tone) is { } problem ? $"fact \"{fact.Label}\": {problem}" : null).FirstOrDefault(problem => problem is not null),
        TimelineType when inTimeline => "a timeline inside a timeline",
        TimelineType when Steps is not { Count: > 0 } => "it has no steps",
        TimelineType => StepsProblem(Steps),
        _ => null,
    };

    private static string? StepsProblem(IReadOnlyList<TimelineStep> steps)
    {
        DateTimeOffset? last = null;
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var where = $"step {at + 1}, \"{step.Label}\"";
            if (step.When < last)
            {
                return $"{where}: steps run oldest first";
            }

            last = step.When ?? last;
            if (Tones.Problem(step.Tone) is { } tone)
            {
                return $"{where}: {tone}";
            }

            var problem = step.Blocks.Select((block, index) => block.Problem(inTimeline: true) is { } found ? $"{where}, block {index + 1}, {block.Type}: {found}" : null)
                .FirstOrDefault(found => found is not null);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }
}

/// <summary>The tones a fact or a step may carry: good, bad or neutral. None reads as neutral.</summary>
internal static class Tones
{
    private static readonly string[] Known = ["good", "bad", "neutral"];

    public static string? Problem(string? tone) =>
        tone is null || Known.Contains(tone, StringComparer.Ordinal) ? null : $"a tone is good, bad or neutral, not \"{tone}\"";
}

/// <summary>One file of a diff: an added file has Before "", a removed one After "".</summary>
internal sealed record DiffFile(string Path, string Before, string After);

internal sealed record Claim(string Text, bool Holds, string? Evidence = null);

/// <summary>
/// A span of a code block that adds its weight to the block's tally. Lines are the file's own, like the block's
/// startLine. Columns count UTF-16 code units from 1, and the end column is one past the span. A mark may hold marks
/// nested inside it. Text, when given, is what the source says the span reads. In a context, a mark may carry the
/// queries a click on it runs, and the id of the section a click on it jumps to.
/// </summary>
internal sealed record CodeMark(
    CodePosition Start,
    CodePosition End,
    string Label,
    decimal Weight,
    string? Note = null,
    string? Text = null,
    IReadOnlyList<CodeMark>? Marks = null,
    IReadOnlyList<MarkQuery>? Queries = null,
    string? Section = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

internal sealed record CodePosition(int Line, int Col);

/// <summary>A query a mark offers: the source's label for it and the query a click puts in the context's bar and runs.</summary>
internal sealed record MarkQuery(string Label, string Query);

/// <summary>A code block's score: its base plus the weights of all its marks, nested ones too, should come to Value.</summary>
internal sealed record Tally(string Label, decimal Base, decimal Value)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

/// <summary>A labelled value the window shows as the source wrote it. brain does no arithmetic on it.</summary>
internal sealed record Fact(string Label, string Value, string? Tone = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

/// <summary>One step of a timeline, oldest first: a label, its blocks, and when it was if the source knows.</summary>
internal sealed record TimelineStep(string Label, IReadOnlyList<QuestionBlock> Blocks, DateTimeOffset? When = null, string? Tone = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

/// <summary>
/// The JSON a source prints, read strictly: every field a record names without a default must be there, and none of
/// them may be null.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SourceReply))]
internal sealed partial class QuestionJson : JsonSerializerContext;
