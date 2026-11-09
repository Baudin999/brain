namespace Brain;

/// <summary>One edge as a node file writes it: <c>- rel -> target</c>.</summary>
internal sealed record Edge(string Rel, string Target);
