namespace Brain;

/// <summary>Reads a ref as written into a node ref or a typed ref.</summary>
internal static class Ref
{
    public const char Separator = ':';

    /// <summary>
    /// A prefix that is a typed ref kind makes a typed ref; any other prefix names a node, in a scope or in a project
    /// registered under that name. brain init refuses a project name that is a scope or a kind.
    /// </summary>
    public static IRef Parse(string text)
    {
        var colon = text.IndexOf(Separator, StringComparison.Ordinal);
        if (colon < 0)
        {
            return new NodeRef(text, Scope: null, text);
        }

        var prefix = text[..colon];
        var rest = text[(colon + 1)..];
        return TypedRefKinds.IsKind(prefix) ? new TypedRef(text, prefix, rest) : new NodeRef(text, prefix, rest);
    }
}
