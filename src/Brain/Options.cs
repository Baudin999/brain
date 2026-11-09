namespace Brain;

/// <summary>A command's arguments split into positionals, <c>--name value</c> options and bare <c>--flag</c>s.</summary>
internal sealed class Options
{
    private const string Prefix = "--";
    private readonly List<string> positional = [];
    private readonly Dictionary<string, string> named = new(StringComparer.Ordinal);
    private readonly HashSet<string> flagged = new(StringComparer.Ordinal);

    private Options()
    {
    }

    public IReadOnlyList<string> Positional => positional;

    /// <summary>Parses, refusing an option not in <paramref name="allowed"/> or one without a value.</summary>
    public static Options Parse(IReadOnlyList<string> arguments, params string[] allowed) =>
        ParseFlagged(arguments, [], allowed);

    /// <summary>Parses like <see cref="Parse"/>, and also takes the <paramref name="flags"/>, which carry no value.</summary>
    public static Options ParseFlagged(IReadOnlyList<string> arguments, IReadOnlyList<string> flags, params string[] allowed)
    {
        var options = new Options();
        for (var at = 0; at < arguments.Count; at++)
        {
            at = options.Read(arguments, at, flags, allowed);
        }

        return options;
    }

    public string? Get(string name) => named.GetValueOrDefault(name);

    public bool Has(string flag) => flagged.Contains(flag);

    /// <summary>Refuses every argument, for a command that takes none.</summary>
    public static void ThrowIfAny(IReadOnlyList<string> arguments, string usage) => Parse(arguments).Expect(0, usage);

    public IReadOnlyList<string> Expect(int count, string usage) =>
        Positional.Count == count ? Positional : throw BrainException.Usage(usage);

    /// <summary>Reads the argument at <paramref name="at"/> and returns the index of the last argument it used.</summary>
    private int Read(IReadOnlyList<string> arguments, int at, IReadOnlyList<string> flags, string[] allowed)
    {
        var argument = arguments[at];
        var name = argument[Math.Min(Prefix.Length, argument.Length)..];
        if (!argument.StartsWith(Prefix, StringComparison.Ordinal))
        {
            positional.Add(argument);
            return at;
        }

        if (flags.Contains(name, StringComparer.Ordinal))
        {
            flagged.Add(name);
            return at;
        }

        if (!allowed.Contains(name, StringComparer.Ordinal) || at + 1 >= arguments.Count)
        {
            throw BrainException.WrongCommandLine($"unknown option or missing value: {argument}");
        }

        named[name] = arguments[at + 1];
        return at + 1;
    }
}
