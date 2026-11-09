using System.Globalization;

namespace Brain;

/// <summary>Creates a node file, or refuses with the reason.</summary>
internal static class NodeCreator
{
    private const string NamerSeparator = ", ";

    public static bool Exists(Graph graph, NewNode spec) =>
        graph.Find(new NodeRef(spec.Slug, spec.Scope, spec.Slug)) is not null;

    public static PlacedNode Create(Graph graph, NewNode spec, DateOnly today)
    {
        var node = Draft(graph, spec, today);
        graph.Save(node);
        return node;
    }

    /// <summary>The node Create would write, refused as Create refuses it, and nothing written.</summary>
    public static PlacedNode Draft(Graph graph, NewNode spec, DateOnly today) => new(Check(graph, spec), Build(spec, today));

    /// <summary>Refuses a node Create would refuse, and returns its scope, so a command can check before it writes anything else.</summary>
    public static Scope Check(Graph graph, NewNode spec)
    {
        var scope = graph.Scopes.Require(spec.Scope);
        BrainException.ThrowIf(Slugs.IsValid(spec.Slug) ? null : $"slug {spec.Slug} is not lowercase-hyphenated");
        BrainException.ThrowIf(Titles.Problem(spec.Title));
        BrainException.ThrowIf(Titles.TextProblem(NodeParts.Body, spec.Body));
        BrainException.ThrowIf(Titles.TextProblem(NodeParts.Why, spec.Why));
        BrainException.ThrowIf(FieldNames.Namers.Contains(spec.NamedBy, StringComparer.Ordinal) ? null : $"named-by must be {string.Join(NamerSeparator, FieldNames.Namers)}");
        BrainException.ThrowIf(spec.Kind.Length > 0 ? null : $"kind is empty");
        BrainException.ThrowIf(Exists(graph, spec) ? $"{NodeRef.Qualified(spec.Scope, spec.Slug)} already exists" : null);
        return scope;
    }

    private static Node Build(NewNode spec, DateOnly today)
    {
        List<NodeField> fields =
        [
            new(FieldNames.Kind, spec.Kind),
            new(FieldNames.Subjects, Subjects.Format(spec.Subjects)),
        ];
        fields.Add(new NodeField(FieldNames.NamedBy, spec.NamedBy));
        fields.Add(new NodeField(FieldNames.Born, today.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture)));
        if (!string.IsNullOrEmpty(spec.Session))
        {
            fields.Add(new NodeField(FieldNames.Session, spec.Session));
        }

        return new Node(spec.Slug, spec.Title.Trim(), fields, [], spec.Body.Trim(), spec.Why.Trim(), []);
    }
}
