using Brain.Questions;

namespace Brain.Serving;

/// <summary>
/// The inboxes of brain's window beside node review: one per source in <c>sources.tsv</c>. Listing runs every source's
/// command at once and shows each source's open questions, minus those the user answered already, each with the
/// project its repo is in projects.tsv. A source that fails shows why, and never takes another source or node review
/// down. An answer is taken only for a question and a choice the window was last shown, and is written once. A
/// question's focus lines are lit in its own code blocks as it is listed.
/// </summary>
internal sealed class Inboxes(BrainEnv env, TimeProvider time)
{
    private readonly Lock shown = new();
    private readonly AnswerStore answers = new(env.Home);
    private Dictionary<string, IReadOnlyList<Question>> listed = new(StringComparer.Ordinal);

    public IReadOnlyList<SourceView> List()
    {
        var sources = Sources.All(env.Home);
        var replies = new (IReadOnlyList<Question>? Questions, string? Error)[sources.Count];
        Parallel.For(0, sources.Count, index => replies[index] = Run(sources[index]));
        var projects = ProjectRegistry.All(env.Home).ToList();
        var views = new List<SourceView>();
        var now = new Dictionary<string, IReadOnlyList<Question>>(StringComparer.Ordinal);
        for (var index = 0; index < sources.Count; index++)
        {
            var name = sources[index].Name;
            var open = replies[index].Questions?.Where(question => !answers.Has(name, question.Id)).ToList();
            if (open is not null)
            {
                now[name] = open;
            }

            views.Add(new SourceView(name, replies[index].Error, [.. (open ?? []).Select(question => new QuestionView(Lit(question), Graphs(projects, question.Repo), Drift.Of(question.Blocks), Drift.Sums(question.Blocks), Drift.Agreements(question.Blocks)))]));
        }

        lock (shown)
        {
            listed = now;
        }

        return views;
    }

    /// <summary>Writes the user's answer to a question the window showed; throws a refusal for anything else.</summary>
    public SourceAnswer Answer(string source, string id, string choice, string? words)
    {
        lock (shown)
        {
            if (!listed.TryGetValue(source, out var questions) || questions.FirstOrDefault(open => open.Id == id) is not { } question)
            {
                throw new BrainException($"{source} has no open question {id} in this window; refresh the inboxes");
            }

            BrainException.ThrowIf(question.Choices.Any(offered => offered.Id == choice) ? null : $"{source} question {id} offers no choice {choice}");
            var answer = answers.Write(source, new SourceAnswer(id, choice, string.IsNullOrWhiteSpace(words) ? null : words.Trim(), time.GetUtcNow()));
            listed[source] = [.. questions.Where(open => open.Id != id)];
            return answer;
        }
    }

    private (IReadOnlyList<Question>? Questions, string? Error) Run(Source source)
    {
        if (source.Problem is { } problem)
        {
            return (null, problem);
        }

        try
        {
            return (SourceReply.Parse(source.Name, SourceCommand.Run(source.Command, env.Home)).Questions, null);
        }
        catch (BrainException failure)
        {
            return (null, failure.Message);
        }
    }

    /// <summary>The question with its focus lines added to the highlights of its code blocks.</summary>
    private static Question Lit(Question question)
    {
        var focus = question.Focus ?? [];
        return question with { Blocks = [.. question.Blocks.Select(block => block.Focused(focus))] };
    }

    /// <summary>The project of projects.tsv whose repo is the question's, compared as full paths; none when no project is.</summary>
    private IReadOnlyList<GraphView> Graphs(IEnumerable<RegisteredProject> projects, string repo) =>
        ReviewViews.Graphs(Path.IsPathFullyQualified(repo) ? ProjectRegistry.At(projects, env.UserHome, repo) : null);
}

/// <summary>One inbox: the source's name, why it failed if it did, and its open questions.</summary>
internal sealed record SourceView(string Name, string? Error, IReadOnlyList<QuestionView> Questions);

/// <summary>
/// A question as the source gave it, the project of its repo as the window focuses and colors it, none when no project
/// holds its repo, what in it does not add up, brain's sum for each tally, and whether each facts block's values agree.
/// </summary>
internal sealed record QuestionView(Question Question, IReadOnlyList<GraphView> Graphs, IReadOnlyList<BlockDrift> Drift, IReadOnlyList<BlockSum> Sums, IReadOnlyList<BlockAgreement> Agreements);

/// <summary>A source's context for the window: the commit it read, if it says, and its sections as brain checked them.</summary>
internal sealed record ContextView(string? Commit, IReadOnlyList<SectionView> Sections)
{
    /// <summary>A context as the window draws it, each section checked and the focus lines lit in its code.</summary>
    public static ContextView Of(SourceContext context, IReadOnlyList<FocusLines> focus) =>
        new(context.Commit, [.. context.Sections.Select(section => SectionView.Of(section, focus))]);
}

/// <summary>
/// A context's section as the source gave it, without its children, which follow as views of their own: what in it does
/// not add up, brain's sums and agreements. The question's focus lines are highlighted in its code blocks, and a
/// section that holds one, or a descendant that does, starts open.
/// </summary>
internal sealed record SectionView(
    ContextSection Section,
    IReadOnlyList<BlockDrift> Drift,
    IReadOnlyList<BlockSum> Sums,
    IReadOnlyList<BlockAgreement> Agreements,
    IReadOnlyList<SectionView> Children)
{
    public static SectionView Of(ContextSection section, IReadOnlyList<FocusLines> focus)
    {
        var shown = section with
        {
            Blocks = [.. section.Blocks.Select(block => block.Focused(focus))],
            Open = section.Open || section.Tree().Any(one => one.Blocks.Any(block => !ReferenceEquals(block.Focused(focus), block))),
            Sections = null,
        };
        return new SectionView(shown, Questions.Drift.Of(section.Blocks), Questions.Drift.Sums(section.Blocks), Questions.Drift.Agreements(section.Blocks), [.. (section.Sections ?? []).Select(child => Of(child, focus))]);
    }
}

internal sealed record QuestionAnswerRequest(string Source, string Id, string Choice, string? Words);
