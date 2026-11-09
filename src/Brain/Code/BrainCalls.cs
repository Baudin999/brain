namespace Brain.Code;

/// <summary>
/// The brain queries a transcript has called and not yet seen the result of, by their call's id. A result that answers
/// one comes back with the nodes it lists.
/// </summary>
internal sealed class BrainCalls
{
    private readonly Dictionary<string, BrainQuery> queries = new(StringComparer.Ordinal);

    /// <summary>Forgets every query, as a transcript written anew does.</summary>
    public void Clear() => queries.Clear();

    /// <summary>
    /// <paramref name="one"/> as it is, after remembering it when it is a call of a brain query; a result of such a query
    /// with the nodes it lists.
    /// </summary>
    public CodeEvent Itemised(CodeEvent one)
    {
        if (one.Id is null)
        {
            return one;
        }

        if (one.Brain is not null)
        {
            queries[one.Id] = new BrainQuery(one.Brain, one.Subject ?? string.Empty);
            return one;
        }

        if (one.Kind == CodeEvent.Result && queries.TryGetValue(one.Id, out var query))
        {
            return one with { Items = query.Items(one.Text ?? string.Empty) };
        }

        return one;
    }
}
