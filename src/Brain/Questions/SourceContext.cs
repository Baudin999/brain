using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brain.Questions;

/// <summary>
/// What a source's context command prints for a query: the commit it read, if it says, and its sections in order, each
/// with an id, a title, whether it starts open, blocks of the kinds a question holds and its children. brain reads the
/// shape and none of the meaning: a section's title and every block are the source's text, which the window shows as it
/// is.
/// </summary>
internal sealed record SourceContext(IReadOnlyList<ContextSection> Sections, string? Commit = null)
{
    /// <summary>
    /// Runs the source's context command in the given directory, the question's repo, with the query's words after it,
    /// each word an argument of its own that no shell parses, and reads what it printed. Throws a refusal that says
    /// why, with what the command said on stderr, when it fails, runs late or prints what brain cannot read.
    /// </summary>
    public static SourceContext Run(Source source, string query, string directory, TimeSpan timeout)
    {
        var command = source.Context ?? throw new BrainException($"{Sources.FileName} gives {source.Name} no context command");
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Parse(SourceCommand.Run(command, directory, words, timeout));
    }

    /// <summary>Reads and checks a context; throws a refusal that names the first thing wrong with it.</summary>
    public static SourceContext Parse(string json)
    {
        SourceContext? context;
        try
        {
            context = JsonSerializer.Deserialize(json, ContextJson.Default.SourceContext);
        }
        catch (JsonException error)
        {
            throw new BrainException($"it printed invalid JSON: {error.Message}");
        }

        context = context ?? throw new BrainException("it printed null");
        BrainException.ThrowIf(context.Problem());
        return context;
    }

    private string? Problem()
    {
        var all = Sections.SelectMany(section => section.Tree()).ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in all)
        {
            if (string.IsNullOrWhiteSpace(section.Id))
            {
                return $"section \"{section.Title}\" has no id";
            }

            if (!ids.Add(section.Id))
            {
                return $"it lists section \"{section.Id}\" twice";
            }
        }

        foreach (var section in all)
        {
            var problem = section.Blocks.Select((block, index) => block.Problem() is { } found ? $"block {index + 1}, {block.Type}: {found}" : null)
                .Concat(section.Blocks.SelectMany(block => Marks(block.Marks ?? [])).Select(mark => MarkProblem(mark, ids)))
                .FirstOrDefault(found => found is not null);
            if (problem is not null)
            {
                return $"section \"{section.Id}\": {problem}";
            }
        }

        return null;
    }

    private static IEnumerable<CodeMark> Marks(IReadOnlyList<CodeMark> marks) => marks.SelectMany(mark => Marks(mark.Marks ?? []).Prepend(mark));

    private static string? MarkProblem(CodeMark mark, HashSet<string> ids)
    {
        if (mark.Section is { } section && !ids.Contains(section))
        {
            return $"mark \"{mark.Label}\" jumps to section \"{section}\", which the context does not hold";
        }

        return mark.Queries?.Any(query => string.IsNullOrWhiteSpace(query.Query)) == true ? $"mark \"{mark.Label}\" offers an empty query" : null;
    }
}

/// <summary>
/// One section of a context: its id, which a mark jumps to, its title, whether it starts open, its blocks and its
/// children, which nest without limit. Ids are unique across the whole tree, and a mark may link to a section at any
/// depth.
/// </summary>
internal sealed record ContextSection(string Id, string Title, IReadOnlyList<QuestionBlock> Blocks, bool Open = false, IReadOnlyList<ContextSection>? Sections = null)
{
    /// <summary>The section, then each of its descendants, parents before children.</summary>
    public IEnumerable<ContextSection> Tree() => (Sections ?? []).SelectMany(child => child.Tree()).Prepend(this);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rest { get; set; }
}

/// <summary>A context command's JSON, read as strictly as a source's questions.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SourceContext))]
internal sealed partial class ContextJson : JsonSerializerContext;
