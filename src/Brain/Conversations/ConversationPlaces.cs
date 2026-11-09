namespace Brain.Conversations;

/// <summary>
/// Where one scope keeps its conversations: its name, as a conversation's ref starts with it, its kind, the folder, the
/// repo a sitting's agent works in, the project's or the data repo, and why it cannot hold one when it cannot.
/// </summary>
internal sealed record ConversationPlace(string Name, string Kind, string Dir, string Repo, string? Problem);

/// <summary>
/// Every scope has its own conversations, outside <c>graph/</c>, so lookup and check never see them. Personal and global
/// keep theirs in the data repo's <c>conversations/personal</c> and <c>conversations/global</c>; a project in its
/// <c>docs/brain/conversations</c>, named by its name in projects.tsv.
/// </summary>
internal static class ConversationPlaces
{
    public const string DirName = "conversations";

    /// <summary>Personal, global, then every project in projects.tsv, in its order.</summary>
    public static IReadOnlyList<ConversationPlace> All(BrainEnv env) =>
        [Brain(env.Home, ScopeNames.Personal), Brain(env.Home, ScopeNames.Global), .. ProjectRegistry.All(env.Home).Select(project => Project(env, project))];

    /// <summary>
    /// The place <paramref name="name"/> names: personal, global, a project of projects.tsv, or project for the one in
    /// effect. Refuses a name that is none of them and a place that cannot hold a conversation.
    /// </summary>
    public static ConversationPlace Named(BrainEnv env, string name)
    {
        var place = name switch
        {
            ScopeNames.Personal or ScopeNames.Global => Brain(env.Home, name),
            ScopeNames.Project => InEffect(env),
            _ => ProjectRegistry.Named(env.Home, name) is { } project
                ? Project(env, project)
                : throw new BrainException($"{name} is not personal, global, project or a project in projects.tsv"),
        };
        BrainException.ThrowIf(place.Problem);
        return place;
    }

    private static ConversationPlace InEffect(BrainEnv env)
    {
        var scope = ScopeResolver.InEffect(env).FirstOrDefault(scope => scope.Name == ScopeNames.Project)
            ?? throw new BrainException("no project scope is in effect here; name a project from projects.tsv");
        var name = ProjectRegistry.NameOf(env.Home, env.UserHome, scope.Repo!)
            ?? throw new BrainException($"{scope.Repo} is not in projects.tsv, so its conversations would have no name; run brain init there");
        return Project(env, ProjectRegistry.Named(env.Home, name)!);
    }

    private static ConversationPlace Brain(string home, string name) =>
        new(name, name, Path.Combine(home, DirName, name), home, Problem: null);

    private static ConversationPlace Project(BrainEnv env, RegisteredProject project)
    {
        var root = project.Root(env.UserHome);
        var docs = ProjectScaffold.Root(root);
        var problem = !Directory.Exists(root) ? $"project {project.Name} is not cloned here: {root}"
            : !Directory.Exists(ProjectScaffold.GraphDir(root)) ? $"project {project.Name} has no docs/brain; run brain init there"
            : null;
        return new ConversationPlace(project.Name, ScopeNames.Project, Path.Combine(docs, DirName), root, problem);
    }
}
