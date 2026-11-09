using Brain.Migrating;

namespace Brain.Commands;

/// <summary>
/// Moves every node file of the scopes in effect from the old one-line label to frontmatter, title, body and why. It is
/// mechanical: no model and no judge. A label splits at its first "because" into title and why, the notes become the
/// body, and the fields and edges carry over. A node of the old model moves to information or idea, without phase,
/// superseded-by and commit: edges, as Migration maps it. A file already in the new format and the new model is left
/// alone, so a second run changes nothing. All or nothing: one file it cannot read refuses the run before anything is written. --dry-run prints every
/// node it would write, label, title and why, and writes nothing.
/// </summary>
internal static class MigrateCommand
{
    public const string Name = "migrate";
    private const string DryRun = "dry-run";
    private const string Indent = "  ";
    private const string LabelPrefix = Indent + "label: ";
    private const string TitlePrefix = Indent + "title: ";
    private const string WhyPrefix = Indent + "why:   ";
    private const string WouldMigrate = "would migrate";
    private const string Migrated = "migrated";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [DryRun]);
        options.Expect(0, Help.UsageOf(Name));
        var dry = options.Has(DryRun);
        var current = 0;
        var moved = new List<(Scope Scope, Node Node, string Label)>();
        var rekinded = new List<(Scope Scope, Node Node, string Kind)>();
        foreach (var scope in ScopeResolver.InEffect(env).Where(scope => Directory.Exists(scope.GraphDir)))
        {
            foreach (var path in Directory.EnumerateFiles(scope.GraphDir, Layout.NodePattern).Order(StringComparer.Ordinal))
            {
                var text = File.ReadAllText(path);
                if (LegacyNode.IsCurrent(text))
                {
                    var parsed = NodeParser.Parse(Path.GetFileNameWithoutExtension(path), text);
                    if (Migration.ToModel(parsed) is { } model)
                    {
                        rekinded.Add((scope, model, Was(parsed)));
                    }
                    else
                    {
                        current++;
                    }

                    continue;
                }

                var (node, label) = LegacyNode.Convert(Path.GetFileNameWithoutExtension(path), text);
                moved.Add((scope, Migration.ToModel(node) ?? node, label));
            }
        }

        foreach (var (scope, node, label) in moved)
        {
            if (dry)
            {
                env.Out.WriteLine(NodeRef.Qualified(scope.Name, node.Slug));
                env.Out.WriteLine(LabelPrefix + label);
                env.Out.WriteLine(TitlePrefix + node.Title);
                env.Out.WriteLine(WhyPrefix + node.Why);
            }
            else
            {
                NodeStore.Write(scope, node);
            }
        }

        foreach (var (scope, node, kind) in rekinded)
        {
            if (dry)
            {
                env.Out.WriteLine($"{NodeRef.Qualified(scope.Name, node.Slug)}: {kind} -> {node.Field(FieldNames.Kind)}");
            }
            else
            {
                NodeStore.Write(scope, node);
            }
        }

        var why = moved.Count(move => move.Node.Why.Length > 0);
        var verb = dry ? WouldMigrate : Migrated;
        env.Out.WriteLine($"migrate: {verb} {moved.Count} nodes, {why} with a why and {moved.Count - why} without; {rekinded.Count} moved to the new model; {current} already in the new format");
        return ExitCodes.Ok;
    }

    /// <summary>The old kind and phase, as the dry run names where a node came from.</summary>
    private static string Was(Node node) =>
        $"{node.Field(FieldNames.Kind)} {node.Field(Migration.Phase)}".TrimEnd();
}
