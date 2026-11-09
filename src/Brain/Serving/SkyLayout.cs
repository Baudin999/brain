using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Brain.Serving;

/// <summary>
/// Where each node of the sky lies, the same for the same graph on every visit. Personal is the core and global the
/// ring around it. The projects lie around the ring, the largest placed first: each tries the spots of a golden-angle
/// spread and takes the one that keeps it nearest the ring and nearest the projects it shares edges with, so the
/// projects spread around the core, a smaller one fits between larger ones, and few edges between projects cross the
/// middle. A project's place answers only to the sizes of the projects and the edges between them. Each cluster grows
/// the way a graph does: its nodes are placed in the order they entered git, each in the room its cluster had when it
/// entered, so the oldest knowledge lies inside and the newest at the rim. The ring grows around the room of an empty
/// core and is then pushed straight out whole, a gap past the core's farthest node, so as the core grows every global
/// node keeps its angle and its depth in the ring, and global moves no personal node. A node that entered with an edge
/// to an older node of its own cluster settles beside those neighbours, any other at a spot its ref hashes to, and each
/// takes the first spot from there that keeps its room from every node placed before it, past the room's rim when the
/// room is full. A cluster's first nodes keep a wider room, so a small cluster, which barely swells, reads star by
/// star. A node that entered with more neighbours settles nearer its cluster's heart, and a node that entered with none
/// leans toward the rim. Last, each cluster swells from its middle out by a stretch that depends on a spot alone, so a
/// large cluster reads like a galaxy: a dense core and a sparse halo. The stretch only pushes nodes apart, so no two
/// come closer than their room. Nothing a node does moves an older node, so a new node finds its place and the rest of
/// its cluster stays as it was; a growing cluster only drifts whole. A link made later moves no node: it is only drawn.
/// An edge between clusters places no node, only where its clusters lie.
/// </summary>
internal static class SkyLayout
{
    /// <summary>A cluster's radius grows by this many times the square root of its node count, so each node gets about as much room.</summary>
    private const double Cell = 15;

    /// <summary>
    /// How close two nodes of one cluster may stand. It is less than a cell, so a cluster's room never fills and a node
    /// finds a spot near where it wants to be.
    /// </summary>
    internal const double Room = Cell * 0.9;

    /// <summary>
    /// How many of a cluster's first nodes keep the wider <see cref="ElderRoom"/> from every node placed before them, so a
    /// small cluster, which barely swells, reads star by star at the whole sky. The nodes after them keep only
    /// <see cref="Room"/>, so a large cluster's core grows dense.
    /// </summary>
    private const int Elders = 64;

    /// <summary>The room of a cluster's first <see cref="Elders"/> nodes, and the cell they take in the cluster's room as much wider.</summary>
    internal const double ElderRoom = Room * Wide;

    /// <summary>How many times <see cref="Room"/> an elder keeps.</summary>
    private const double Wide = 2;

    /// <summary>How far from its neighbours a node first tries to settle, at least; its ref adds up to as much again, so a hub's neighbours spread out around it.</summary>
    private const double Beside = Cell * 1.6;

    /// <summary>A node with <c>k</c> older neighbours wants a spot <c>Heart / (Heart + k - 1)</c> as far from its cluster's heart as their middle, so a well-connected node settles nearer the heart.</summary>
    private const double Heart = 4;

    /// <summary>
    /// The scale of a cluster's stretch: a spot <c>r</c> out from its middle moves to <c>r × (1 + r / Swell)</c>, so the
    /// nodes this far out stand twice as far apart as at the heart. A small cluster barely swells and a large one gets a
    /// sparse halo.
    /// </summary>
    private const double Swell = 600;

    /// <summary>The empty space between the core and the ring, and between the ring and the clusters.</summary>
    internal const double Gap = 40;

    /// <summary>The ring is this much wider than its node count needs, so it reads as a ring and not a thin line.</summary>
    private const double RingRoom = 2;

    /// <summary>How far a project steps out when it would overlap a project placed before it.</summary>
    private const double Step = 10;

    /// <summary>How many spots of the golden-angle spread a project tries around the ring.</summary>
    private const int Spots = 144;

    /// <summary>How many spots a node tries inside its cluster's room, on a spiral out from where it wants to be, before it looks past the rim.</summary>
    private const int Tries = 400;

    /// <summary>A node's hashed spot leans toward its room's rim by this root of an even spread, so a node that entered without an edge lies in the halo.</summary>
    private const double Lean = 2;

    private const double Turn = 2 * Math.PI;
    private const double Golden = 2.399963229728653;
    private const double Unit = 4294967296.0;

    /// <summary>The room of personal and of each project: a disc.</summary>
    private static readonly Area Disc = new(0, Cell * Cell);

    /// <summary>The room of global before it is pushed out past the core: a ring around the room of an empty core.</summary>
    private static readonly Area Band = new(Disc.Edge(0), RingRoom * Cell * Cell);

    /// <summary>A graph to place: its tier and how many nodes it holds.</summary>
    internal sealed record Group(string Tier, int Count);

    /// <summary>
    /// A node to place: its ref, which seeds its spot, its group by place in the group list, and when it entered its
    /// graph, a place that only grows. Personal and global count their entries as one, since they share a repo. Nodes
    /// that entered together are placed by ref.
    /// </summary>
    internal sealed record Star(string Ref, int Group, int Entered);

    /// <summary>Where each group and each star lies, and how far out the ring's inner edge lies from its middle.</summary>
    internal sealed record Placed(IReadOnlyList<(double X, double Y, double Radius)> Groups, IReadOnlyList<(double X, double Y)> Stars, double Inner);

    /// <summary>
    /// Places the stars. <paramref name="beside"/> are the edges each node entered with, which settle the younger end
    /// beside the older; <paramref name="edges"/> are the edges as they are now, which place only the projects.
    /// </summary>
    public static Placed Place(IReadOnlyList<Group> groups, IReadOnlyList<Star> stars, IReadOnlyList<(int From, int To)> beside, IReadOnlyList<(int From, int To)> edges)
    {
        var order = Enumerable.Range(0, stars.Count)
            .OrderBy(index => stars[index].Entered)
            .ThenBy(index => stars[index].Ref, StringComparer.Ordinal)
            .ToList();
        var rank = new int[stars.Count];
        for (var i = 0; i < order.Count; i++)
        {
            rank[order[i]] = i;
        }

        var neighbours = stars.Select(_ => new List<int>()).ToArray();
        foreach (var (from, to) in beside.Where(edge => edge.From != edge.To && stars[edge.From].Group == stars[edge.To].Group))
        {
            neighbours[from].Add(to);
            neighbours[to].Add(from);
        }

        // Each cluster is laid out around its own middle, so where the cluster lies changes nothing inside it.
        var taken = groups.Select(_ => new Taken()).ToArray();
        var count = new int[groups.Count];
        var local = new (double X, double Y)[stars.Count];
        foreach (var index in order)
        {
            var star = stars[index];
            var born = ++count[star.Group];
            var area = groups[star.Group].Tier == ScopeNames.Global ? Band : Disc;
            var (spread, angle) = Hash(star.Ref);
            var older = neighbours[index].Where(other => rank[other] < rank[index]).Distinct().ToList();
            var heart = Heart / (Heart + older.Count - 1);
            var apart = Beside * (1 + spread);
            var wanted = older.Count == 0
                ? area.At(born, spread, angle)
                : ((older.Average(other => local[other].X) * heart) + (apart * Math.Cos(angle)), (older.Average(other => local[other].Y) * heart) + (apart * Math.Sin(angle)));
            local[index] = Free(area, born, taken[star.Group], wanted, angle, born <= Elders ? ElderRoom : Room);
            taken[star.Group].Add(local[index]);
        }

        // A group reaches a node's room past its farthest node, or across the room it had with no node at all, swollen
        // as its nodes are.
        var reach = groups.Select(group => Swollen((group.Tier == ScopeNames.Global ? Band : Disc).Edge(0))).ToArray();
        for (var i = 0; i < stars.Count; i++)
        {
            local[i] = Swollen(local[i]);
            reach[stars[i].Group] = Math.Max(reach[stars[i].Group], Distance(local[i], (0, 0)) + Room);
        }

        // The ring is pushed out whole until its inner edge lies a gap past the core's reach. The core reaches at least
        // as far as the ring's inner edge, the swollen room of an empty core, so the push is never less than the gap,
        // and a push straight out by one distance never brings two spots closer.
        var inner = Swollen(Band.Inner);
        var core = Enumerable.Range(0, groups.Count).Where(index => groups[index].Tier == ScopeNames.Personal).Select(index => reach[index]).DefaultIfEmpty(inner).Max();
        var push = core + Gap - inner;
        for (var i = 0; i < stars.Count; i++)
        {
            if (groups[stars[i].Group].Tier == ScopeNames.Global)
            {
                local[i] = Pushed(local[i], push);
            }
        }

        for (var index = 0; index < groups.Count; index++)
        {
            if (groups[index].Tier == ScopeNames.Global)
            {
                reach[index] += push;
            }
        }

        var middles = Middles(groups, reach, stars, edges);
        return new Placed(
            [.. groups.Select((group, index) => (middles[index].X, middles[index].Y, reach[index]))],
            [.. stars.Select((star, index) => (middles[star.Group].X + local[index].X, middles[star.Group].Y + local[index].Y))],
            inner + push);
    }

    /// <summary>
    /// The first clear spot on a spiral out from <paramref name="wanted"/>: inside the room the cluster had for the first
    /// tries, then past its rim, so every node gets a spot that keeps <paramref name="room"/> from every node placed
    /// before it.
    /// </summary>
    private static (double X, double Y) Free(Area area, int born, Taken taken, (double X, double Y) wanted, double angle, double room)
    {
        var spot = area.Keep(born, wanted);
        for (var turn = 1; !taken.Clear(spot, room); turn++)
        {
            var distance = room * 0.6 * Math.Sqrt(turn);
            var tried = (wanted.X + (distance * Math.Cos(angle + (turn * Golden))), wanted.Y + (distance * Math.Sin(angle + (turn * Golden))));
            spot = turn <= Tries ? area.Keep(born, tried) : area.Outside(tried);
        }

        return spot;
    }

    /// <summary>
    /// A spot of a cluster, moved out from its middle by the stretch. The stretch moves a spot <c>r</c> out to
    /// <c>f(r) = r × (1 + r / Swell)</c>, and since both f′ and f(r) / r are at least 1, it never brings two spots closer.
    /// </summary>
    private static (double X, double Y) Swollen((double X, double Y) spot)
    {
        var scale = 1 + (Distance(spot, (0, 0)) / Swell);
        return (spot.X * scale, spot.Y * scale);
    }

    private static double Swollen(double distance) => distance * (1 + (distance / Swell));

    /// <summary>A spot of the ring, moved straight out from the middle by <paramref name="push"/>, keeping its angle.</summary>
    private static (double X, double Y) Pushed((double X, double Y) spot, double push)
    {
        var distance = Distance(spot, (0, 0));
        var scale = (distance + push) / distance;
        return (spot.X * scale, spot.Y * scale);
    }

    /// <summary>
    /// The middle of each group: personal and global at the centre, and each project beyond the ring, the largest first.
    /// Each project tries the spots of a golden-angle spread, at each stepped outward past every project placed before
    /// it, and takes the spot nearest the centre plus its share of the way to each placed project it has edges with.
    /// </summary>
    private static List<(double X, double Y)> Middles(IReadOnlyList<Group> groups, double[] reach, IReadOnlyList<Star> stars, IReadOnlyList<(int From, int To)> edges)
    {
        var ring = groups.Select((group, index) => group.Tier == ScopeNames.Global ? reach[index] : 0).DefaultIfEmpty(0).Max();
        var links = groups.Select(_ => new int[groups.Count]).ToArray();
        foreach (var (from, to) in edges)
        {
            var (a, b) = (stars[from].Group, stars[to].Group);
            if (a != b && groups[a].Tier == ScopeNames.Project && groups[b].Tier == ScopeNames.Project)
            {
                links[a][b]++;
                links[b][a]++;
            }
        }

        var middles = groups.Select(_ => (X: 0.0, Y: 0.0)).ToList();
        var placed = new List<int>();
        var order = Enumerable.Range(0, groups.Count)
            .Where(index => groups[index].Tier == ScopeNames.Project)
            .OrderByDescending(index => groups[index].Count)
            .ThenBy(index => index);
        foreach (var index in order)
        {
            var radius = reach[index];
            var bridges = Enumerable.Range(0, groups.Count).Sum(other => links[index][other]);
            (double X, double Y) best = default;
            var lowest = double.MaxValue;
            for (var spot = 0; spot < Spots; spot++)
            {
                var angle = spot * Golden;
                var distance = ring + Gap + radius;
                while (placed.Any(other => Distance(Along(angle, distance), middles[other]) < radius + reach[other] + Gap))
                {
                    distance += Step;
                }

                var middle = Along(angle, distance);
                var cost = distance + (bridges == 0 ? 0 : placed.Sum(other => links[index][other] * Distance(middle, middles[other])) / bridges);
                if (cost < lowest)
                {
                    (best, lowest) = (middle, cost);
                }
            }

            middles[index] = best;
            placed.Add(index);
        }

        return middles;
    }

    private static (double X, double Y) Along(double angle, double distance) => (distance * Math.Cos(angle), distance * Math.Sin(angle));

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>Two numbers in [0, 1) and [0, 2π) from a ref alone, so a node's spot moves no other node's.</summary>
    private static (double Spread, double Angle) Hash(string reference)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(reference));
        return (BinaryPrimitives.ReadUInt32BigEndian(hash) / Unit, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(sizeof(uint))) / Unit * Turn);
    }

    /// <summary>
    /// The room of a cluster around its own middle, as it grows: with <c>n</c> nodes it reaches out to the square root of
    /// Inner² + PerNode × n, a disc when Inner is 0 and a ring otherwise, each of the first <see cref="Elders"/> counting
    /// as Wide² nodes.
    /// </summary>
    private sealed record Area(double Inner, double PerNode)
    {
        /// <summary>How far the room reaches with <paramref name="count"/> nodes, a node's room past the last node's spot.</summary>
        public double Edge(int count) => Reach(count) + Room;

        /// <summary>The spot <paramref name="spread"/> of the way out and at <paramref name="angle"/> in the room of <paramref name="born"/> nodes, leaning toward its rim.</summary>
        public (double X, double Y) At(int born, double spread, double angle)
        {
            var reach = Reach(born);
            var distance = Math.Sqrt((Inner * Inner) + (Math.Pow(spread, 1 / Lean) * ((reach * reach) - (Inner * Inner))));
            return (distance * Math.Cos(angle), distance * Math.Sin(angle));
        }

        /// <summary>The point, moved back into the room of <paramref name="born"/> nodes when it strayed out.</summary>
        public (double X, double Y) Keep(int born, (double X, double Y) point)
        {
            var distance = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            if (distance < 1e-9)
            {
                return Inner > 0 ? (Inner, 0) : point;
            }

            var kept = Math.Clamp(distance, Inner, Edge(born));
            return kept == distance ? point : (point.X * kept / distance, point.Y * kept / distance);
        }

        /// <summary>The point, moved out of the hole of a ring when it strayed in, however far out it lies.</summary>
        public (double X, double Y) Outside((double X, double Y) point)
        {
            var distance = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            return distance >= Inner ? point : distance < 1e-9 ? (Inner, 0) : (point.X * Inner / distance, point.Y * Inner / distance);
        }

        private double Reach(int count)
        {
            var nodes = Math.Max(count, 1);
            return Math.Sqrt((Inner * Inner) + (PerNode * ((Wide * Wide * Math.Min(nodes, Elders)) + Math.Max(0, nodes - Elders))));
        }
    }

    /// <summary>
    /// The spots placed so far, filed by a square of an elder's room, so whether a spot keeps its room asks only the
    /// nine squares around it and not every node placed.
    /// </summary>
    private sealed class Taken
    {
        private readonly Dictionary<(long, long), List<(double X, double Y)>> squares = [];

        public void Add((double X, double Y) spot)
        {
            var square = Square(spot);
            if (!squares.TryGetValue(square, out var spots))
            {
                spots = [];
                squares[square] = spots;
            }

            spots.Add(spot);
        }

        /// <summary>True when no spot placed so far lies nearer than <paramref name="room"/>, at most an elder's room.</summary>
        public bool Clear((double X, double Y) spot, double room)
        {
            var (x, y) = Square(spot);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (squares.TryGetValue((x + dx, y + dy), out var spots) && spots.Exists(other => Distance(other, spot) < room))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static (long, long) Square((double X, double Y) spot) => ((long)Math.Floor(spot.X / ElderRoom), (long)Math.Floor(spot.Y / ElderRoom));
    }
}
