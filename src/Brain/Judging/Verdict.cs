using System.Text.Json;

namespace Brain.Judging;

/// <summary>
/// The judge's answer: whether the entries pass, and why not when they don't. Duplicates and Contradictions name each
/// entry that says the same as, or contradicts, one of its nearest entries; Edges name the edges each entry should have
/// to its nearest entries. The gate refuses on any duplicate or contradiction, whatever ok says, and drops a duplicate
/// whose entry and node also stand under contradictions.
/// </summary>
internal sealed record Verdict(bool Ok, string Reason)
{
    private const char Open = '{';
    private const char Close = '}';
    private const string OkProperty = "ok";
    private const string ReasonProperty = "reason";
    private const string DuplicatesProperty = "duplicates";
    private const string ContradictionsProperty = "contradictions";
    private const string EdgesProperty = "edges";
    private const string EntryProperty = "entry";
    private const string NodeProperty = "node";
    private const string WhatProperty = "what";
    private const string RelProperty = "rel";
    private const string ToProperty = "to";

    public static Verdict Accept { get; } = new(Ok: true, string.Empty);

    public IReadOnlyList<Duplicate> Duplicates { get; init; } = [];

    public IReadOnlyList<Contradiction> Contradictions { get; init; } = [];

    public IReadOnlyList<NamedEdge> Edges { get; init; } = [];

    /// <summary>
    /// The verdict in a model's answer: the JSON object between its first <c>{</c> and its last <c>}</c>, with a
    /// boolean ok and a string reason, and the lists of duplicates, contradictions and edges when it names any. A
    /// refusal needs a reason the writer can act on. Null when the answer is not one.
    /// </summary>
    public static Verdict? Parse(string answer)
    {
        var start = answer.IndexOf(Open, StringComparison.Ordinal);
        var end = answer.LastIndexOf(Close);
        if (start < 0 || end < start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            return Read(json.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Verdict? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(OkProperty, out var ok)
            || !root.TryGetProperty(ReasonProperty, out var reason))
        {
            return null;
        }

        var passed = ok.ValueKind == JsonValueKind.True;
        var why = reason.ValueKind == JsonValueKind.String ? reason.GetString()!.Trim() : string.Empty;
        var known = ok.ValueKind is JsonValueKind.True or JsonValueKind.False;
        if (!known || (!passed && why.Length == 0))
        {
            return null;
        }

        var duplicates = List(root, DuplicatesProperty, [EntryProperty, NodeProperty], fields => new Duplicate(fields[0], fields[1]));
        var contradictions = List(root, ContradictionsProperty, [EntryProperty, NodeProperty, WhatProperty], fields => new Contradiction(fields[0], fields[1], fields[2]));
        var edges = List(root, EdgesProperty, [EntryProperty, RelProperty, ToProperty], fields => new NamedEdge(fields[0], fields[1], fields[2]));
        if (duplicates is null || contradictions is null || edges is null)
        {
            return null;
        }

        return new Verdict(passed, why) { Duplicates = duplicates, Contradictions = contradictions, Edges = edges };
    }

    /// <summary>
    /// The objects of the array <paramref name="name"/>, each read from its string <paramref name="fields"/>; none when
    /// the answer leaves the array out, and null when it is not an array of such objects.
    /// </summary>
    private static List<T>? List<T>(JsonElement root, string name, string[] fields, Func<string[], T> make)
    {
        if (!root.TryGetProperty(name, out var array))
        {
            return [];
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<T>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new string[fields.Length];
            for (var at = 0; at < fields.Length; at++)
            {
                if (!item.TryGetProperty(fields[at], out var value) || value.ValueKind != JsonValueKind.String || value.GetString()!.Trim().Length == 0)
                {
                    return null;
                }

                values[at] = value.GetString()!.Trim();
            }

            items.Add(make(values));
        }

        return items;
    }
}

/// <summary>An entry, by its ref, that says the same as the nearest entry <paramref name="Node"/>.</summary>
internal sealed record Duplicate(string Entry, string Node);

/// <summary>An entry, by its ref, that contradicts the nearest entry <paramref name="Node"/>, and what the contradiction is.</summary>
internal sealed record Contradiction(string Entry, string Node, string What);

/// <summary>An edge the judge says an entry, by its ref, should have: its relation and its target, a nearest entry.</summary>
internal sealed record NamedEdge(string Entry, string Rel, string To);
