using System.Globalization;
using System.Text;

namespace Brain.Judging;

/// <summary>
/// The fixed prompt the judge model reads: the writing rules, that the free rules hold the structure, the meaning it
/// judges, the subjects and the nearest entries it judges each entry against, the entries with their subjects, edges
/// and nearest entries, the subjects in use in each scope, and the one-line answer it must give.
/// </summary>
internal static class JudgePrompt
{
    public const string System = "You judge entries for a knowledge graph. You answer with one line of JSON and nothing else.";

    private const string Preamble =
        "Only lasting subjects belong in this knowledge graph. These are its writing rules:";

    private const string StructureHolds =
        "Free rules have already checked the structure of every entry, and it holds: the kind, a why present on "
        + "information, that no title repeats a commit subject, that every subject is a lowercase-hyphenated word, and that "
        + "every edge target exists. Never refuse for structure.";

    private const string SubjectRule =
        "An entry's subjects are the keywords a search finds it by. Refuse an entry whose subjects do not say what it is "
        + "about, or would not find it in a search, naming the subject and why. When a subject is a near-synonym of a "
        + "subject in use in its scope, listed below, name the word in use instead.";

    private const string NearestRule =
        "Each entry lists its nearest entries: the nodes already in the graph that lie nearest to it. Use them to keep "
        + "the graph free of duplicates and contradictions. When an entry says the same as one of its nearest entries, "
        + "name both under duplicates and refuse. When an entry contradicts one of its nearest entries, name both and the "
        + "contradiction under contradictions and refuse. A duplicate and a contradiction exclude each other: for one "
        + "nearest entry, name at most one of them. An entry that disagrees with a nearest entry contradicts it and does "
        + "not say the same, so name it under contradictions only. Under edges, name each of its nearest entries the "
        + "entry should have an edge to, with a lowercase-hyphenated relation such as part-of, refines, follows, respects "
        + "or because, whether or not the entry carries edges. Name an edge only to a nearest entry, by its ref as "
        + "listed, and never to an entry with no nearest entries.";

    private const string MeaningRule =
        "Judge meaning only. Refuse an entry that is a work log, a finding or open work instead of a subject that lasts. "
        + "Examples are good. A body may illustrate its subject with examples, including concrete situations that happened, when they show what the subject looks like in practice so a reader can recognise it. Never refuse an entry for carrying examples. "
        + "Rejected options and what an idea rests on are good, in information and in ideas alike, when each carries its reason. Never refuse an entry for listing what we rejected and why. "
        + "A log is bad. Refuse an entry whose point is recording what happened, when, or by whom, such as a list of changes, commits, fixes or steps taken, instead of a subject that lasts. "
        + "An entry has three parts: a title that names the subject, an optional body that says what it is, and a why. "
        + "An entry is information, what is true now, or an idea, a record of thinking together: what was discussed, "
        + "what was rejected, and the reasons. An idea needs no why. For information, the why is its why part alone; never read a reason "
        + "into the title or the body. It is real when it names a cause, a cost or a consequence a reader could check or dispute, however "
        + "short. It is not real when it only restates the title in other words. When a why is not real, refuse and "
        + "tell the writer to ask the user for the reason. Never suggest a reason, give an example of one, or rewrite "
        + "the why: an invented reason is worse than none.";

    private const string Instruction =
        "Refuse when an entry fails on meaning. Accept when every entry is a lasting subject with a real why where it needs one. "
        + "Answer with exactly one line of JSON and nothing else: {\"ok\":true,\"reason\":\"\",\"duplicates\":[],"
        + "\"contradictions\":[],\"edges\":[]} to accept, or {\"ok\":false,\"reason\":\"...\",...} to refuse, where the "
        + "reason names the entry, the rule it breaks, and what the writer must do. The reason never proposes a why. "
        + "duplicates holds {\"entry\":\"<entry ref>\",\"node\":\"<nearest ref>\"}, contradictions holds "
        + "{\"entry\":\"<entry ref>\",\"node\":\"<nearest ref>\",\"what\":\"<the contradiction>\"}, and edges holds "
        + "{\"entry\":\"<entry ref>\",\"rel\":\"<relation>\",\"to\":\"<nearest ref>\"}.";

    private const string InUseHeading = "Subjects in use in ";
    private const string NoSubjects = "none";
    private const string EntriesHeading = "Entries:";
    private const string EntryHeading = "Entry ";
    private const string RefField = "ref: ";
    private const string ScopeField = "scope: ";
    private const string SlugField = "slug: ";
    private const string SubjectsField = FieldNames.Subjects + ": ";
    private const string EdgesField = "edges:";
    private const string NoEdges = "edges: none";
    private const string NearestHeading = "Nearest entry ";
    private const string NoNearest = "nearest entries: none";
    private const string Of = " of entry ";
    private const string Colon = ": ";
    private const string Join = ", ";
    private const string KindField = "kind: ";
    private const string TitleField = NodeParts.Title + ": ";
    private const string BodyField = NodeParts.Body + ":";
    private const string WhyField = NodeParts.Why + ":";

    public static string Build(string rules, IReadOnlyList<JudgedEntry> entries)
    {
        var prompt = new StringBuilder()
            .AppendLine(Preamble)
            .AppendLine()
            .AppendLine(rules)
            .AppendLine()
            .AppendLine(StructureHolds)
            .AppendLine()
            .AppendLine(MeaningRule)
            .AppendLine()
            .AppendLine(SubjectRule)
            .AppendLine()
            .AppendLine(NearestRule);
        foreach (var scope in entries.DistinctBy(entry => entry.Scope, StringComparer.Ordinal))
        {
            AppendInUse(prompt, scope);
        }

        prompt.AppendLine().AppendLine(EntriesHeading);
        for (var index = 0; index < entries.Count; index++)
        {
            Append(prompt, index + 1, entries[index]);
        }

        return prompt.AppendLine().AppendLine(Instruction).ToString();
    }

    /// <summary>The subjects the nodes of the entry's scope carry, on one line, the most used first.</summary>
    private static void AppendInUse(StringBuilder prompt, JudgedEntry entry)
    {
        var words = entry.InUse.Count == 0 ? NoSubjects : string.Join(Join, entry.InUse);
        prompt.AppendLine().AppendLine(InUseHeading + entry.Scope + Colon + words);
    }

    private static void Append(StringBuilder prompt, int number, JudgedEntry entry)
    {
        var numbered = number.ToString(CultureInfo.InvariantCulture);
        prompt
            .AppendLine()
            .AppendLine(EntryHeading + numbered)
            .AppendLine(RefField + entry.Ref)
            .AppendLine(ScopeField + entry.Scope)
            .AppendLine(SlugField + entry.Slug)
            .AppendLine(KindField + entry.Kind)
            .AppendLine(TitleField + entry.Title);
        Part(prompt, BodyField, entry.Body);
        Part(prompt, WhyField, entry.Why);
        prompt.AppendLine(SubjectsField + Subjects.Format(entry.Subjects));
        AppendEdges(prompt, entry.Edges);
        for (var index = 0; index < entry.Nearest.Count; index++)
        {
            var near = entry.Nearest[index];
            prompt
                .AppendLine()
                .AppendLine(NearestHeading + (index + 1).ToString(CultureInfo.InvariantCulture) + Of + numbered)
                .AppendLine(RefField + near.Ref)
                .AppendLine(KindField + (near.Node.Field(FieldNames.Kind) ?? string.Empty))
                .AppendLine(TitleField + near.Node.Title);
            Part(prompt, BodyField, near.Node.Body);
            Part(prompt, WhyField, near.Node.Why);
            prompt.AppendLine(SubjectsField + Subjects.Format(Subjects.Of(near.Node)));
        }

        if (entry.Nearest.Count == 0)
        {
            prompt.AppendLine(NoNearest);
        }
    }

    private static void AppendEdges(StringBuilder prompt, IReadOnlyList<Edge> edges)
    {
        if (edges.Count == 0)
        {
            prompt.AppendLine(NoEdges);
            return;
        }

        prompt.AppendLine(EdgesField);
        foreach (var edge in edges)
        {
            prompt.AppendLine(NodeSyntax.ItemPrefix + edge.Rel + NodeSyntax.Arrow + edge.Target);
        }
    }

    /// <summary>A written part under its own heading line, left out when it is empty.</summary>
    private static void Part(StringBuilder prompt, string heading, string text)
    {
        if (text.Trim().Length > 0)
        {
            prompt.AppendLine(heading).AppendLine(text.TrimEnd());
        }
    }
}
