namespace Brain.Commands;

/// <summary>Keeps lines while they fit a character budget. Once one line misses, every later line is left out too.</summary>
internal sealed class Budget(int limit)
{
    private readonly List<string> kept = [];
    private int used;

    public IReadOnlyList<string> Kept => kept;

    public int LeftOut { get; private set; }

    public bool Offer(string line)
    {
        var cost = line.Length + 1;
        if (LeftOut == 0 && used + cost <= limit)
        {
            kept.Add(line);
            used += cost;
            return true;
        }

        LeftOut++;
        return false;
    }
}
