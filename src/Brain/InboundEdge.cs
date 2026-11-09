namespace Brain;

/// <summary>An edge of another node that points at a node, such as one about to be forgotten, written the way faded writes an edge.</summary>
internal sealed record InboundEdge(PlacedNode From, Edge Edge, PlacedNode Target)
{
    public string Text => $"{From.Ref} -{Edge.Rel}-> {Target.Ref}";
}
