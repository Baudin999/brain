namespace Brain;

/// <summary>One header line of a node, <c>- Name: value</c>, kept in file order.</summary>
internal sealed record NodeField(string Name, string Value);
