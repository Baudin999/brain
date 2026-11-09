namespace Brain;

/// <summary>
/// One line of a scope's edge log: an edge and the decayed sum of its travels as of <see cref="AsOf"/>. Decay is
/// exponential, so the sum on any later day is <see cref="Weight"/> times the decay since <see cref="AsOf"/>.
/// </summary>
internal sealed record FoldedEdge(string From, string Rel, string To, double Weight, DateOnly AsOf)
{
    public string Key => EdgeLog.Key(From, Rel, To);

    /// <summary>The decayed sum of the travels on <paramref name="today"/>.</summary>
    public double On(DateOnly today) => Weight * Decay.Of(AsOf, today);

    /// <summary>This edge after one more travel on <paramref name="today"/>.</summary>
    public FoldedEdge Travelled(DateOnly today) =>
        this with { Weight = On(today) + 1, AsOf = today > AsOf ? today : AsOf };
}
