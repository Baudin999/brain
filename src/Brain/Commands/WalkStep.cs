namespace Brain.Commands;

/// <summary>One edge walked: how many hops out it is, and the neighbour it shows.</summary>
internal sealed record WalkStep(int Hop, Neighbour Neighbour);
