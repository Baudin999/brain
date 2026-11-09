using System.Text;
using System.Text.RegularExpressions;

namespace Brain.Tests;

/// <summary>
/// The tags the window's lit-html templates write, as the gates read them. A template is each html`...` in a script, a
/// template inside another's interpolation included. Each tag keeps where it starts in the script, its name and its
/// attributes; an attribute keeps its literal text and the source of each interpolation in its value.
/// </summary>
internal static partial class LitTemplates
{
    /// <summary>Every tag of every template in js, each where it starts in js.</summary>
    public static IEnumerable<Tag> Tags(string js)
    {
        foreach (var template in Bodies(js))
        {
            foreach (Match tag in TagPattern().Matches(template.Blanked))
            {
                var attributes = tag.Groups["attributes"];
                var at = template.Start + attributes.Index;
                var read = AttributePattern().Matches(attributes.Value).Select((attribute) => Read(js, template, at, attribute)).ToList();
                yield return new Tag(template.Start + tag.Index, tag.Groups["tag"].Value, read);
            }
        }
    }

    /// <summary>The script with the text of every template blanked out, its newlines kept, so a line number still points into it.</summary>
    public static string WithoutTemplates(string js)
    {
        var text = new StringBuilder(js);
        foreach (var template in Bodies(js))
        {
            for (var at = template.Start; at < template.Start + template.Blanked.Length; at++)
            {
                if (text[at] != '\n')
                {
                    text[at] = ' ';
                }
            }
        }

        return text.ToString();
    }

    private static Attribute Read(string js, Body template, int at, Match attribute)
    {
        var name = attribute.Groups["name"].Value;
        var value = attribute.Groups["value"];
        if (!value.Success)
        {
            return new Attribute(name, "", [], false);
        }

        var start = at + value.Index;
        var end = start + value.Length;
        var holes = template.Holes
            .Where((hole) => hole.Start >= start && hole.End <= end)
            .Select((hole) => js[(hole.Start + 2)..(hole.End - 1)])
            .ToList();
        var text = value.Value.Replace('\0', ' ');
        var quoted = attribute.Groups["quote"].Success;
        return new Attribute(name, text, holes, quoted);
    }

    /// <summary>
    /// The body of every template in js, from past its opening backtick to its closing one: its text with each
    /// interpolation blanked to NULs but its newlines, and where each interpolation starts and ends in js.
    /// </summary>
    private static IEnumerable<Body> Bodies(string js)
    {
        foreach (Match open in TemplateOpen().Matches(js))
        {
            var start = open.Index + open.Length;
            var holes = new List<(int Start, int End)>();
            var end = TemplateEnd(js, start, holes);
            var blanked = new StringBuilder(js[start..end]);
            foreach (var (holeStart, holeEnd) in holes)
            {
                for (var at = holeStart; at < holeEnd; at++)
                {
                    if (js[at] != '\n')
                    {
                        blanked[at - start] = '\0';
                    }
                }
            }

            yield return new Body(start, blanked.ToString(), holes);
        }
    }

    /// <summary>Where the template whose text starts at at ends, at its closing backtick; it adds each interpolation it holds.</summary>
    private static int TemplateEnd(string js, int at, List<(int Start, int End)>? holes)
    {
        while (at < js.Length && js[at] != '`')
        {
            if (js[at] == '\\')
            {
                at += 2;
                continue;
            }

            if (js[at] == '$' && at + 1 < js.Length && js[at + 1] == '{')
            {
                var end = HoleEnd(js, at + 2);
                holes?.Add((at, end));
                at = end;
                continue;
            }

            at++;
        }

        return Math.Min(at, js.Length);
    }

    /// <summary>Past the brace that closes an interpolation whose code starts at at, its strings and templates skipped whole.</summary>
    private static int HoleEnd(string js, int at)
    {
        var depth = 0;
        while (at < js.Length)
        {
            var c = js[at];
            if (c is '\'' or '"')
            {
                at = StringEnd(js, at) + 1;
                continue;
            }

            if (c == '`')
            {
                at = TemplateEnd(js, at + 1, null) + 1;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    return at + 1;
                }

                depth--;
            }

            at++;
        }

        return js.Length;
    }

    /// <summary>The closing quote of the string whose opening quote is at at.</summary>
    private static int StringEnd(string js, int at)
    {
        var quote = js[at];
        at++;
        while (at < js.Length && js[at] != quote)
        {
            at += js[at] == '\\' ? 2 : 1;
        }

        return Math.Min(at, js.Length);
    }

    /// <summary>A template the html tag opens, up to its backtick.</summary>
    [GeneratedRegex(@"\bhtml`", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateOpen();

    [GeneratedRegex(@"<(?<tag>[a-zA-Z][\w-]*)(?<attributes>[^>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>An attribute: its name, with lit-html's @, . or ? kept, and its value, quoted or bare.</summary>
    [GeneratedRegex(@"(?<name>[^\s""'>/=\0]+)(?:\s*=\s*(?:(?<quote>"")(?<value>[^""]*)""|(?<quote>')(?<value>[^']*)'|(?<value>[^\s""'>]+)))?", RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();

    /// <summary>A tag a template writes: where it starts in the script, its name and its attributes.</summary>
    internal sealed record Tag(int Index, string Name, List<Attribute> Attributes)
    {
        public Attribute? this[string name] => Attributes.Find((attribute) => attribute.Name == name);
    }

    /// <summary>
    /// An attribute of a tag: its name, its value's literal text with each interpolation a space, the source of each
    /// interpolation in its value, and whether the value is quoted.
    /// </summary>
    internal sealed record Attribute(string Name, string Text, List<string> Holes, bool Quoted);

    private sealed record Body(int Start, string Blanked, List<(int Start, int End)> Holes);
}
