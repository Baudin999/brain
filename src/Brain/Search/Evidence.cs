namespace Brain.Search;

/// <summary>How sure the engine is of a hit: sure, likely, or weak, which nobody is shown.</summary>
internal enum Tier
{
    Sure,
    Likely,
    Weak,
}

/// <summary>A tier as lookup prints it and the window shows it.</summary>
internal static class Tiers
{
    public static string Name(this Tier tier) => tier.ToString().ToLowerInvariant();
}

/// <summary>
/// A hit's cosine to the query and the bars of the model that gave it, so a score is only ever compared against its
/// own model's bars.
/// </summary>
internal sealed record Meaning(float Cosine, Bars Bars)
{
    public bool IsClose => Cosine >= Bars.Close;

    public bool Agrees => Cosine >= Bars.Agree;

    public bool IsMeant => Cosine >= Bars.Meant;

    /// <summary>True when the cosine and half the word share together reach the blend.</summary>
    public bool Blends(double words) => Cosine + (words / 2) >= Bars.Blend;
}

/// <summary>
/// What search knows of one hit before fusion turns it into a rank. Exact: its ref, slug or title is the query.
/// Structure: its title or slug starts with the query, or it carries the query or a query word as a subject. Words: the
/// share of the query's weight its text holds, each query word weighted by how rare it is among the candidates, so a
/// word no node holds weighs most and a word nearly every node holds weighs nearly nothing. Meaning: how near its
/// meaning lies to the query's, null without the embedding model.
/// </summary>
internal sealed record Evidence(bool Exact, bool Structure, double Words, Meaning? Meaning)
{
    /// <summary>
    /// The share of the query's weight that is most of it, for a sure hit when meaning or structure agrees.
    /// </summary>
    public const double Most = 0.8;

    /// <summary>
    /// The share of the query's weight that makes a hit likely by words alone. With the model the search test set
    /// scored the same from 0.4 to 0.6. Without it a lower bar keeps more wanted nodes, but at 0.3 a nonsense query
    /// returned a likely hit, and the nearest nonsense hit held 0.33; 0.4 keeps a margin above it.
    /// </summary>
    public const double Many = 0.4;

    /// <summary>
    /// The tier, from the evidence alone. Sure: the query names it, its meaning is close, or its words hold most of the
    /// query and its meaning or the structure agrees. Likely: its meaning matches, its words hold many of the query's,
    /// the structure found it, or meaning and words together reach the blend. Weak: anything else, such as a node that
    /// holds only a common word of the query. Without the embedding model there is no meaning and words and structure
    /// decide.
    /// </summary>
    public Tier Tier =>
        Exact || Meaning is { IsClose: true } || (Words >= Most && (Meaning is { Agrees: true } || Structure)) ? Tier.Sure
        : Meaning is { IsMeant: true } || Words >= Many || Structure || (Meaning?.Blends(Words) ?? false) ? Tier.Likely
        : Tier.Weak;
}
