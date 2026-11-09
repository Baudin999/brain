using System.Text;

namespace Brain.Stats;

/// <summary>
/// A symbol as Navarone's log names it, such as <c>navarone::Navarone.Cli.Commands.RunAsync(string[], Coverage)</c>: the
/// project, two colons, the namespace, the type and the member, with a method's parameters. A lens names a method as
/// <c>Type.Method</c>, the type's own name and the method's without its parameters or type arguments.
/// </summary>
internal static class NavaroneSymbol
{
    private const string ProjectEnd = "::";

    private static readonly string[] Accessors = ["get", "set", "init"];

    /// <summary>The method as a lens names it, <c>Type.Method</c>; null for a symbol that is no method, such as a class or a property.</summary>
    public static string? Method(string symbol)
    {
        if (!symbol.Contains('(', StringComparison.Ordinal))
        {
            return null;
        }

        var parts = Parts(symbol);
        if (parts.Length < 2)
        {
            return null;
        }

        return $"{parts[^2]}.{parts[^1]}";
    }

    /// <summary>
    /// The symbol in the code's own words: a method as a lens names it, a property's accessor with its property, as
    /// <c>Tier.get</c>, and a type by its own name.
    /// </summary>
    public static string Name(string symbol)
    {
        if (Method(symbol) is { } method)
        {
            return method;
        }

        var parts = Parts(symbol);
        if (parts.Length >= 2 && Accessors.Contains(parts[^1]))
        {
            return $"{parts[^2]}.{parts[^1]}";
        }

        return parts[^1];
    }

    /// <summary>The symbol's dotted names, after its project and before its parameters, without type arguments.</summary>
    private static string[] Parts(string symbol)
    {
        var name = symbol;
        var parameters = name.IndexOf('(', StringComparison.Ordinal);
        if (parameters >= 0)
        {
            name = name[..parameters];
        }

        var project = name.IndexOf(ProjectEnd, StringComparison.Ordinal);
        if (project >= 0)
        {
            name = name[(project + ProjectEnd.Length)..];
        }

        return WithoutTypeArguments(name).Split('.');
    }

    /// <summary>The name with every <c>&lt;...&gt;</c> taken out, nested ones included.</summary>
    private static string WithoutTypeArguments(string name)
    {
        var kept = new StringBuilder();
        var depth = 0;
        foreach (var letter in name)
        {
            if (letter == '<')
            {
                depth++;
                continue;
            }

            if (letter == '>')
            {
                depth--;
                continue;
            }

            if (depth == 0)
            {
                kept.Append(letter);
            }
        }

        return kept.ToString();
    }
}
