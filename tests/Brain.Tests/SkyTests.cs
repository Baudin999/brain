using System.Globalization;
using System.Text.Json;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The constellation on the window's Home: every node and edge of personal, global and each registered project as one
/// sky, laid out the same for the same graph, a new node taking its place without moving the rest, no two nodes too
/// close, the projects spread around the core with linked projects side by side, and a search over every graph that
/// lights every node it finds and travels no edge.
/// </summary>
public sealed class SkyTests : IDisposable
{
    private const string Front = "kind: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-03\n";

    private readonly TempBrain brain = new();
    private readonly string studio;

    public SkyTests()
    {
        brain.Ok("add", "--subjects", "test", "personal", "tea", "The user drinks tea", "--kind", "idea", "--body", "Green tea in the morning.");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Grass grows first", "--kind", "idea", "--body", "Ground cover, then trees.");
        brain.Ok("link", "meadow", "for", "personal:tea");
        studio = brain.MakeRepo("studio-repo");
        brain.Init("studio");
        brain.Ok("add", "--subjects", "test", "project", "cups", "Cups for tea in the studio", "--kind", "idea", "--body", "Blue cups.");
        brain.Ok("link", "cups", "respects", "global:meadow");
        brain.MakeRepo("other-repo");
        brain.Init("other");
        brain.Ok("add", "--subjects", "test", "project", "kettle", "The office kettle", "--kind", "idea");
        brain.Cwd = studio;
    }

    public void Dispose() => brain.Dispose();

    private BrainEnv Env => brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null);

    [Fact]
    public void TheSkyHoldsEveryGraphAndTheEdgesBetweenThem()
    {
        brain.Cwd = brain.Root;

        var view = Sky.Load(Env).View();

        Assert.Equal(["personal", "global", "studio", "other"], view.Clusters.Select(cluster => cluster.Name));
        Assert.Equal(["personal", "global", "project", "project"], view.Clusters.Select(cluster => cluster.Tier));
        Assert.Equal(ReviewViews.Hue("studio"), view.Clusters[2].Hue);
        Assert.Null(view.Clusters[0].Hue);
        Assert.InRange(view.Clusters[1].Inner, view.Clusters[0].Radius, view.Clusters[1].Radius);
        Assert.All(view.Clusters.Where(cluster => cluster.Tier != "global"), cluster => Assert.Equal(0, cluster.Inner));
        Assert.Equal(["global:meadow", "other:kettle", "personal:tea", "studio:cups"], view.Nodes.Select(node => node.Ref));
        Assert.Equal(
            ["global:meadow for personal:tea", "studio:cups respects global:meadow"],
            view.Edges.Select(edge => $"{view.Nodes[edge.From].Ref} {edge.Rel} {view.Nodes[edge.To].Ref}"));
        Assert.All(view.Edges, edge => Assert.True(edge.Weight > 0));
        var kettle = view.Nodes[1];
        Assert.Equal((0, true, "idea", 3), (kettle.Degree, kettle.Bare, kettle.Kind, kettle.Cluster));
        Assert.Equal((2, false), (view.Nodes[0].Degree, view.Nodes[0].Bare));
    }

    [Fact]
    public void AnEdgeIntoNoNodeOfTheSkyStillCountsAsAnEdge()
    {
        brain.Cwd = brain.Root;
        brain.WriteNode("global", "reaching", Front, "Reaches into a project not cloned here", "- uses -> afar:thing\n");

        var reaching = Sky.Load(Env).View().Nodes.Single(node => node.Ref == "global:reaching");

        Assert.Equal(1, reaching.Degree);
    }

    /// <summary>A project edge travelled from inside its repo weighs more in the sky than an edge nobody travelled, born the same day.</summary>
    [Fact]
    public void AProjectEdgeWeighsItsTravels()
    {
        brain.Session = "sky-session";
        brain.Ok("node", "cups");
        brain.Ok("node", "global:meadow");
        brain.Session = null;

        var view = Sky.Load(Env).View();

        double Weight(string from) => view.Edges.Single(edge => view.Nodes[edge.From].Ref == from).Weight;
        Assert.True(Weight("studio:cups") > Weight("global:meadow"), $"travelled {Weight("studio:cups")}, untravelled {Weight("global:meadow")}");
    }

    /// <summary>The sky flags what travelled today, what was born today and the edges <c>brain faded</c> lists, and flagging records nothing.</summary>
    [Fact]
    public void TheSkyFlagsTravelledBornAndFadedOnRead()
    {
        for (var n = 0; n < 12; n++)
        {
            brain.WriteNode("global", $"old-{n}", Front.Replace("2026-10-03", "2026-09-01", StringComparison.Ordinal), $"Old {n}", "- uses -> personal:tea\n");
        }

        brain.Ok("add", "--subjects", "test", "global", "fresh", "Fresh", "--kind", "idea");
        brain.Session = "sky-session";
        brain.Ok("node", "cups");
        brain.Ok("node", "global:meadow");
        brain.Session = null;
        var before = Travels();

        var view = Sky.Load(Env).View();

        string Name(SkyEdgeView edge) => $"{view.Nodes[edge.From].Ref} -{edge.Rel}-> {view.Nodes[edge.To].Ref}";
        var listed = brain.Ok("faded").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[(line.IndexOf("  ", StringComparison.Ordinal) + 2)..]).ToList();
        Assert.Equal(listed.Order(), view.Edges.Where(edge => edge.Faded).Select(Name).Order());
        Assert.Equal(["studio:cups -respects-> global:meadow"], view.Edges.Where(edge => edge.Travelled).Select(Name));
        Assert.Equal(["global:fresh", "global:meadow", "other:kettle", "personal:tea", "studio:cups"], view.Nodes.Where(node => node.Born).Select(node => node.Ref));
        Assert.Equal(before, Travels());
    }

    [Fact]
    public void TheSameGraphGivesTheSameSkyOnEveryVisit()
    {
        var first = JsonSerializer.Serialize(Sky.Load(Env).View(), ReviewJson.Default.SkyView);
        var second = JsonSerializer.Serialize(Sky.Load(Env).View(), ReviewJson.Default.SkyView);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SearchFindsANodeOfAnyProjectAndTravelsNoEdge()
    {
        var before = Travels();

        var found = Sky.Load(Env).Search("kettle");

        Assert.Equal("other:kettle", found.Hits[0].Ref);
        Assert.Equal(before, Travels());
    }

    [Fact]
    public void SearchLightsEveryNodeItFindsNotOnlyTheTopTen()
    {
        brain.Cwd = brain.Root;
        for (var n = 0; n < 14; n++)
        {
            brain.WriteNode("global", $"teapot-{n}", Front, $"Teapot number {n}");
        }

        var found = Sky.Load(Env).Search("teapot");

        Assert.Equal(14, found.Hits.Count(hit => hit.Ref.StartsWith("global:teapot-", StringComparison.Ordinal)));
    }

    /// <summary>A common word in a search lights no node that holds only that word, while every node with the rare one lights.</summary>
    [Fact]
    public void SearchLightsNoNodeThatMatchesOnlyACommonWord()
    {
        brain.Cwd = brain.Root;
        for (var n = 0; n < 6; n++)
        {
            brain.WriteNode("global", $"teapot-{n}", Front, $"The teapot number {n}");
        }

        for (var n = 0; n < 20; n++)
        {
            brain.WriteNode("global", $"plain-{n}", Front, $"The plain note number {n}");
        }

        var found = Sky.Load(Env).Search("the teapot").Hits.Select(hit => hit.Ref).ToList();

        Assert.Equal(6, found.Count(reference => reference.StartsWith("global:teapot-", StringComparison.Ordinal)));
        Assert.DoesNotContain(found, reference => reference.StartsWith("global:plain-", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nodes that enter after the rest, one born the same day with a ref that sorts first and one without a born date, in
    /// personal, global and a project, move no node there before and after they are committed.
    /// </summary>
    [Fact]
    public void NodesThatEnterLaterMoveNoOlderNode()
    {
        Crowd();
        var before = Spots(inside: true);

        brain.WriteNode("personal", "aaa-first", Front, "Sorts first", "- for -> p-3\n");
        brain.WriteNode("global", "aaa-first", Front, "Sorts first", "- for -> g-5\n");
        brain.WriteNode("global", "unborn", "kind: idea\nsubjects: []\nnamed-by: user\n", "Born on no day");
        WriteProjectNode("aaa-first", "Sorts first", "- for -> s-2\n");
        var waiting = Spots(inside: true);
        TempBrain.CommitAll(brain.Home);
        TempBrain.CommitAll(studio);
        var committed = Spots(inside: true);

        Assert.All(before, pair => Assert.True(Distance(pair.Value, waiting[pair.Key]) < 0.15, $"{pair.Key} moved before the commit"));
        Assert.All(before, pair => Assert.True(Distance(pair.Value, committed[pair.Key]) < 0.15, $"{pair.Key} moved after the commit"));
    }

    /// <summary>A link made later between two nodes of one cluster, in personal, global and a project, moves no node before or after it is committed.</summary>
    [Fact]
    public void AnEdgeAddedLaterMovesNoNode()
    {
        Crowd();
        var before = Spots(inside: false);

        brain.Ok("link", "personal:p-25", "for", "personal:p-1");
        brain.Ok("link", "global:g-20", "for", "global:g-3");
        brain.Ok("link", "s-27", "for", "s-4");
        var waiting = Spots(inside: false);
        TempBrain.CommitAll(brain.Home);
        TempBrain.CommitAll(studio);
        var committed = Spots(inside: false);

        Assert.Equal(before, waiting);
        Assert.Equal(before, committed);
    }

    /// <summary>
    /// A node added in the old format takes its place from the commit that added it and its neighbours from the first
    /// commit in which it reads as a node, not from a link made after that.
    /// </summary>
    [Fact]
    public void AnOldFormatNodeEntersWithTheEdgesOfItsFirstReadableCommit()
    {
        var elder = Path.Combine(brain.Home, "graph", "global", "elder.md");
        brain.WriteNode("global", "g-1", Front, "One");
        File.WriteAllText(elder, "# Elder\n\nWritten before the frontmatter.\n\n## Edges\n- for -> g-1\n");
        TempBrain.CommitAll(brain.Home);
        brain.WriteNode("global", "g-2", Front, "Two");
        brain.WriteNode("global", "elder", Front, "Elder", "- for -> g-2\n");
        TempBrain.CommitAll(brain.Home);
        brain.WriteNode("global", "elder", Front, "Elder", "- for -> g-2\n- for -> g-1\n");
        TempBrain.CommitAll(brain.Home);

        var entries = EntryOrder.Of(brain.Home, Path.Combine(brain.Home, "graph"));

        Assert.Equal(entries["global/g-1.md"].Place, entries["global/elder.md"].Place);
        Assert.Equal(["g-2"], entries["global/elder.md"].Edges);
        static string Text(IReadOnlyDictionary<string, Entry> all) =>
            string.Join(' ', all.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}@{pair.Value.Place}:{string.Join(',', pair.Value.Edges ?? [])}"));
        Assert.Equal(Text(entries), Text(EntryOrder.Of(brain.Home, Path.Combine(brain.Home, "graph"))));
    }

    /// <summary>Every global node lies a gap past the farthest personal node and its room, whichever graph is the larger.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 200)]
    [InlineData(30, 25)]
    [InlineData(300, 10)]
    [InlineData(300, 200)]
    public void TheRingLiesWhollyOutsideTheCore(int personal, int global)
    {
        List<SkyLayout.Group> groups = [new("personal", personal), new("global", global)];
        var stars = groups.SelectMany((group, index) => Enumerable.Range(0, group.Count).Select(n => new SkyLayout.Star($"g{index}:node-{n}", index, n))).ToList();
        var edges = Enumerable.Range(1, stars.Count - 1).Select(i => (From: i, To: i - 1 - (i % 3))).Where(edge => edge.To >= 0 && stars[edge.To].Group == stars[edge.From].Group).ToList();

        var placed = SkyLayout.Place(groups, stars, edges, edges);

        var farthest = Enumerable.Range(0, stars.Count).Where(i => stars[i].Group == 0).Max(i => Distance(placed.Stars[i], (0, 0)));
        var nearest = Enumerable.Range(0, stars.Count).Where(i => stars[i].Group == 1).Min(i => Distance(placed.Stars[i], (0, 0)));
        Assert.True(nearest >= farthest + SkyLayout.Room + SkyLayout.Gap - 1e-9, $"the ring starts at {nearest:0.0} and the core reaches {farthest:0.0}");
    }

    /// <summary>A new personal node pushes every global node out by one distance and turns none, and moves no older personal node.</summary>
    [Fact]
    public void ANewPersonalNodePushesTheRingOutWholeAndTurnsNoGlobalNode()
    {
        var (groups, stars, edges) = Galaxy(seed: 4);
        var before = SkyLayout.Place(groups, stars, edges, edges);
        var grown = groups.Select((group, index) => index == 0 ? group with { Count = group.Count + 40 } : group).ToList();
        var later = stars.Max(star => star.Entered) + 1;
        List<SkyLayout.Star> more = [.. stars, .. Enumerable.Range(0, 40).Select(n => new SkyLayout.Star($"g0:aaa-{n}", 0, later))];

        List<(int From, int To)> linked = [.. edges, (stars.Count, 0)];
        var after = SkyLayout.Place(grown, more, linked, linked);

        var ring = Enumerable.Range(0, stars.Count).Where(i => stars[i].Group == 1).ToList();
        var pushes = ring.Select(i => Distance(after.Stars[i], (0, 0)) - Distance(before.Stars[i], (0, 0))).ToList();
        Assert.True(pushes.Min() > 0, $"a global node moved in by {-pushes.Min():0.00}");
        Assert.True(pushes.Max() - pushes.Min() < 1e-9, $"global nodes moved out by {pushes.Min():0.00} to {pushes.Max():0.00}");
        var turned = ring.Max(i => Distance(Heading(before.Stars[i]), Heading(after.Stars[i])));
        Assert.True(turned < 1e-9, $"a global node turned by {turned}");
        var moved = Enumerable.Range(0, stars.Count).Where(i => stars[i].Group == 0).Max(i => Distance(before.Stars[i], after.Stars[i]));
        Assert.True(moved < 1e-9, $"a personal node moved {moved:0.00}");
    }

    [Fact]
    public void ANewGlobalNodeMovesNoPersonalNode()
    {
        var (groups, stars, edges) = Galaxy(seed: 2);
        var before = SkyLayout.Place(groups, stars, edges, edges);
        var grown = groups.Select((group, index) => index == 1 ? group with { Count = group.Count + 40 } : group).ToList();
        List<SkyLayout.Star> more = [.. stars, .. Enumerable.Range(0, 40).Select(n => new SkyLayout.Star($"g1:aaa-{n}", 1, n))];

        var after = SkyLayout.Place(grown, more, edges, edges);

        var moved = Enumerable.Range(0, stars.Count).Where(i => stars[i].Group == 0).Max(i => Distance(before.Stars[i], after.Stars[i]));
        Assert.True(moved < 1e-9, $"a personal node moved {moved:0.00}");
    }

    [Fact]
    public void NoTwoNodesStandCloserThanTheirRoomInALargeCluster()
    {
        List<SkyLayout.Group> groups = [new("personal", 120), new("global", 150), new("project", 900)];
        var stars = groups.SelectMany((group, index) => Enumerable.Range(0, group.Count).Select(n => new SkyLayout.Star($"g{index}:node-{n}", index, n / 3))).ToList();
        var edges = Enumerable.Range(1, stars.Count - 1).Select(i => (From: i, To: i - 1 - (i % 5))).Where(edge => edge.To >= 0 && stars[edge.To].Group == stars[edge.From].Group).ToList();

        var placed = SkyLayout.Place(groups, stars, edges, edges);

        // The ring lies a gap past the core, so personal and global keep their room from each other too and count as one.
        int Layer(int group) => group < 2 ? 0 : group;
        var closest = double.MaxValue;
        for (var a = 0; a < stars.Count; a++)
        {
            for (var b = a + 1; b < stars.Count; b++)
            {
                if (Layer(stars[a].Group) == Layer(stars[b].Group))
                {
                    closest = Math.Min(closest, Distance(placed.Stars[a], placed.Stars[b]));
                }
            }
        }

        Assert.True(closest >= SkyLayout.Room - 1e-9, $"two nodes stand {closest:0.00} apart");
    }

    /// <summary>Every node of a small cluster is one of its first ones, and keeps an elder's wider room from every other, so it reads star by star at the whole sky.</summary>
    [Fact]
    public void NoTwoNodesOfASmallClusterStandCloserThanAnEldersRoom()
    {
        var (groups, stars, edges) = Galaxy(seed: 6);

        var placed = SkyLayout.Place(groups, stars, edges, edges);

        int Layer(int group) => group < 2 ? 0 : group;
        var closest = double.MaxValue;
        for (var a = 0; a < stars.Count; a++)
        {
            for (var b = a + 1; b < stars.Count; b++)
            {
                if (Layer(stars[a].Group) == Layer(stars[b].Group))
                {
                    closest = Math.Min(closest, Distance(placed.Stars[a], placed.Stars[b]));
                }
            }
        }

        Assert.True(SkyLayout.ElderRoom > SkyLayout.Room);
        Assert.True(closest >= SkyLayout.ElderRoom - 1e-9, $"two nodes stand {closest:0.00} apart");
    }

    [Fact]
    public void EveryNodeStaysInItsClusterAndNoTwoProjectsOverlap()
    {
        var (groups, stars, edges) = Galaxy(seed: 7);

        var placed = SkyLayout.Place(groups, stars, edges, edges);

        for (var i = 0; i < stars.Count; i++)
        {
            var (x, y, radius) = placed.Groups[stars[i].Group];
            Assert.True(Distance(placed.Stars[i], (x, y)) <= radius + 1e-6, $"{stars[i].Ref} left its cluster");
        }

        var (_, _, core) = placed.Groups[0];
        var (_, _, ring) = placed.Groups[1];
        Assert.True(core < ring);
        Assert.InRange(placed.Inner, core, ring);
        for (var i = 0; i < stars.Count; i++)
        {
            if (stars[i].Group == 1)
            {
                Assert.True(Distance(placed.Stars[i], (placed.Groups[1].X, placed.Groups[1].Y)) >= placed.Inner - 1e-6, $"{stars[i].Ref} lies inside the ring's inner edge");
            }
        }

        for (var a = 2; a < groups.Count; a++)
        {
            Assert.True(Distance((placed.Groups[a].X, placed.Groups[a].Y), (0, 0)) > ring + placed.Groups[a].Radius, $"project {a} touches the ring");
            for (var b = a + 1; b < groups.Count; b++)
            {
                Assert.True(Distance((placed.Groups[a].X, placed.Groups[a].Y), (placed.Groups[b].X, placed.Groups[b].Y)) > placed.Groups[a].Radius + placed.Groups[b].Radius, $"projects {a} and {b} overlap");
            }
        }
    }

    [Fact]
    public void ANewNodeTakesItsPlaceWithoutMovingTheRestOfItsCluster()
    {
        var (groups, stars, edges) = Galaxy(seed: 3);
        var before = SkyLayout.Place(groups, stars, edges, edges);
        var grown = groups.Select((group, index) => index == 2 ? group with { Count = group.Count + 1 } : group).ToList();

        List<(int From, int To)> linked = [.. edges, (stars.Count, stars.FindIndex(star => star.Group == 2))];
        var after = SkyLayout.Place(grown, [.. stars, new SkyLayout.Star("a:newcomer", 2, stars.Max(star => star.Entered) + 1)], linked, linked);

        var moved = Enumerable.Range(0, stars.Count).Select(i => Distance(Inside(before, stars[i], i), Inside(after, stars[i], i))).ToList();
        Assert.True(moved.Max() < 1e-9, $"a node moved {moved.Max():0.00} in its cluster");
        Assert.True(Distance((before.Groups[2].X, before.Groups[2].Y), (after.Groups[2].X, after.Groups[2].Y)) < 1, "the cluster jumped");
    }

    [Fact]
    public void AnEdgeBetweenTwoProjectsMovesNoNodeInsideEither()
    {
        var (groups, stars, edges) = Galaxy(seed: 5);
        var from = stars.FindIndex(star => star.Group == 2);
        var to = stars.FindIndex(star => star.Group == 4);

        var apart = SkyLayout.Place(groups, stars, edges, edges);
        var bridged = SkyLayout.Place(groups, stars, edges, [.. edges, (from, to)]);

        var moved = Enumerable.Range(0, stars.Count).Select(i => Distance(Inside(apart, stars[i], i), Inside(bridged, stars[i], i))).ToList();
        Assert.True(moved.Max() < 1e-9, $"a node moved {moved.Max():0.00} in its cluster");
    }

    [Fact]
    public void ProjectsWithEdgesBetweenThemLieSideBySide()
    {
        var (groups, stars, edges) = Galaxy(seed: 5);
        var from = stars.FindIndex(star => star.Group == 2);
        var to = stars.FindIndex(star => star.Group == 4);
        double Apart(SkyLayout.Placed placed) => Distance((placed.Groups[2].X, placed.Groups[2].Y), (placed.Groups[4].X, placed.Groups[4].Y));

        var apart = SkyLayout.Place(groups, stars, edges, edges);
        var bridged = SkyLayout.Place(groups, stars, edges, [.. edges, (from, to), (from + 1, to + 1)]);

        Assert.True(Apart(bridged) < Apart(apart), $"bridged {Apart(bridged):0.0}, apart {Apart(apart):0.0}");
        var others = Enumerable.Range(3, groups.Count - 3).Where(other => other != 4);
        Assert.All(others, other => Assert.True(
            Apart(bridged) <= Distance((bridged.Groups[2].X, bridged.Groups[2].Y), (bridged.Groups[other].X, bridged.Groups[other].Y)),
            $"project {other} lies nearer the largest project than its bridged neighbour"));
    }

    /// <summary>
    /// The window's version moves when a node file lands in a project outside the pending queue, as a closed agent's
    /// commit does, and when an edge changes in a scope, so Home reloads its sky; with nothing changed it holds.
    /// </summary>
    [Fact]
    public void TheVersionMovesWhenTheGraphChangesAndHoldsWhenNothingDoes()
    {
        var start = ReviewViews.Version(Env);
        Assert.Equal(start, ReviewViews.Version(Env));

        WriteProjectNode("saucer", "A saucer under each cup", string.Empty);
        var added = ReviewViews.Version(Env);
        Assert.NotEqual(start, added);
        Assert.Equal(added, ReviewViews.Version(Env));

        brain.Cwd = brain.Root;
        brain.Ok("link", "personal:tea", "respects", "global:meadow");
        Assert.NotEqual(added, ReviewViews.Version(Env));
    }

    /// <summary>The projects spread around the core, and the largest, placed first, lies as near the ring as any.</summary>
    [Fact]
    public void ProjectsSpreadAroundTheCoreTheLargestNearestIt()
    {
        List<SkyLayout.Group> groups = [new("personal", 30), new("global", 25), .. Enumerable.Range(0, 6).Select(n => new SkyLayout.Group("project", 10 + (n * 10)))];
        var stars = groups.SelectMany((group, index) => Enumerable.Range(0, group.Count).Select(n => new SkyLayout.Star($"g{index}:node-{n}", index, n))).ToList();

        var placed = SkyLayout.Place(groups, stars, [], []);

        var projects = placed.Groups.Skip(2).ToList();
        var middle = (projects.Average(group => group.X), projects.Average(group => group.Y));
        var reach = projects.Average(group => Distance((group.X, group.Y), (0, 0)));
        Assert.True(Distance(middle, (0, 0)) < reach / 3, $"the projects lean {Distance(middle, (0, 0)):0.0} to one side of {reach:0.0}");
        double Gap((double X, double Y, double Radius) group) => Distance((group.X, group.Y), (0, 0)) - group.Radius;
        Assert.All(projects, project => Assert.True(Gap(placed.Groups[^1]) <= Gap(project) + 1e-9, "a smaller project lies nearer the ring than the largest"));
    }

    /// <summary>
    /// A large cluster reads like a galaxy: its nodes lie denser at its heart than in its halo, and the nodes that entered
    /// without an edge lie further out than the ones that entered with neighbours.
    /// </summary>
    [Fact]
    public void ALargeClusterHasADenseCoreAndASparseHalo()
    {
        List<SkyLayout.Group> groups = [new("personal", 1), new("global", 1), new("project", 400)];
        var stars = groups.SelectMany((group, index) => Enumerable.Range(0, group.Count).Select(n => new SkyLayout.Star($"g{index}:node-{n}", index, n))).ToList();
        var first = stars.FindIndex(star => star.Group == 2);
        var edges = Enumerable.Range(first + 1, 399).Where(i => i % 4 > 0).Select(i => (From: i, To: first + ((i * 7) % (i - first)))).ToList();

        var placed = SkyLayout.Place(groups, stars, edges, edges);

        var (x, y, radius) = placed.Groups[2];
        var distances = Enumerable.Range(first, 400).ToDictionary(i => i, i => Distance(placed.Stars[i], (x, y)));
        var heart = distances.Values.Count(distance => distance < radius / 2) / Math.Pow(radius / 2, 2);
        var halo = distances.Values.Count(distance => distance >= radius / 2) / ((radius * radius) - Math.Pow(radius / 2, 2));
        Assert.True(heart > 6 * halo, $"the heart holds {heart * 1e4:0.00} and the halo {halo * 1e4:0.00} per square of 100");
        var linked = edges.Select(edge => edge.From).ToHashSet();
        var alone = distances.Where(pair => pair.Key != first && !linked.Contains(pair.Key)).Average(pair => pair.Value);
        var near = distances.Where(pair => linked.Contains(pair.Key)).Average(pair => pair.Value);
        Assert.True(alone > near, $"the nodes without an edge lie {alone:0.0} out, those with one {near:0.0}");
    }

    /// <summary>A personal core, a global ring and four projects, each node with an edge or two inside its own graph.</summary>
    private static (List<SkyLayout.Group> Groups, List<SkyLayout.Star> Stars, List<(int From, int To)> Edges) Galaxy(int seed)
    {
        List<SkyLayout.Group> groups = [new("personal", 30), new("global", 25), new("project", 60), new("project", 20), new("project", 5), new("project", 12)];
        var stars = groups.SelectMany((group, index) => Enumerable.Range(0, group.Count).Select(n => new SkyLayout.Star($"g{index}:node-{n}", index, n))).ToList();
        var edges = new List<(int From, int To)>();
        for (var i = 0; i < stars.Count; i++)
        {
            var mates = Enumerable.Range(0, stars.Count).Where(j => j != i && stars[j].Group == stars[i].Group).ToList();
            if ((i + seed) % 3 > 0 && mates.Count > 0)
            {
                edges.Add((i, mates[((i * 7) + seed) % mates.Count]));
            }
        }

        return (groups, stars, edges);
    }

    /// <summary>Thirty nodes in each of personal, global and studio, each but the first with an edge to an older one, all committed.</summary>
    private void Crowd()
    {
        for (var n = 0; n < 30; n++)
        {
            var edge = n == 0 ? string.Empty : $"- for -> {{0}}-{((n * 5) + 3) % n}\n";
            brain.WriteNode("personal", $"p-{n}", Front, $"Personal {n}", string.Format(CultureInfo.InvariantCulture, edge, "p"));
            brain.WriteNode("global", $"g-{n}", Front, $"Global {n}", string.Format(CultureInfo.InvariantCulture, edge, "g"));
            WriteProjectNode($"s-{n}", $"Studio {n}", string.Format(CultureInfo.InvariantCulture, edge, "s"));
        }

        TempBrain.CommitAll(brain.Home);
        TempBrain.CommitAll(studio);
    }

    private void WriteProjectNode(string slug, string title, string edges) =>
        File.WriteAllText(Path.Combine(studio, "docs", "brain", "graph", slug + ".md"), $"---\n{Front}---\n# {title}\n\n## Edges\n{edges}");

    /// <summary>Each node's spot in the sky by ref, relative to its cluster's middle when <paramref name="inside"/>.</summary>
    private Dictionary<string, (double X, double Y)> Spots(bool inside)
    {
        var view = Sky.Load(Env).View();
        return view.Nodes.ToDictionary(
            node => node.Ref,
            node => inside ? (node.X - view.Clusters[node.Cluster].X, node.Y - view.Clusters[node.Cluster].Y) : (node.X, node.Y),
            StringComparer.Ordinal);
    }

    /// <summary>A node's spot relative to its cluster's middle.</summary>
    private static (double X, double Y) Inside(SkyLayout.Placed placed, SkyLayout.Star star, int index) =>
        (placed.Stars[index].X - placed.Groups[star.Group].X, placed.Stars[index].Y - placed.Groups[star.Group].Y);

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>Which way a spot lies from the sky's middle, as a point on the unit circle.</summary>
    private static (double X, double Y) Heading((double X, double Y) spot)
    {
        var distance = Distance(spot, (0, 0));
        return (spot.X / distance, spot.Y / distance);
    }

    /// <summary>What records a travel: the edge logs and the session trail, each file's bytes by its path.</summary>
    private Dictionary<string, string> Travels()
    {
        var cache = Path.Combine(brain.Home, "cache");
        var traversals = Path.Combine(cache, "traversals");
        var trail = Path.Combine(cache, "trail.sqlite");
        return (Directory.Exists(traversals) ? Directory.GetFiles(traversals) : [])
            .Append(trail)
            .Where(File.Exists)
            .ToDictionary(path => path, path => Convert.ToHexString(File.ReadAllBytes(path)), StringComparer.Ordinal);
    }
}
