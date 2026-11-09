namespace Brain;

/// <summary>Finds a scope in effect by its name.</summary>
internal static class ScopeLookup
{
    public static Scope? Named(this IReadOnlyList<Scope> scopes, string name) =>
        scopes.FirstOrDefault(scope => string.Equals(scope.Name, name, StringComparison.Ordinal));

    public static Scope Require(this IReadOnlyList<Scope> scopes, string name) =>
        scopes.Named(name) ?? throw new BrainException($"scope {name} is not in effect here");
}
