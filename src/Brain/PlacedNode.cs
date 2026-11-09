namespace Brain;

/// <summary>A node together with the scope that owns it.</summary>
internal sealed record PlacedNode(Scope Scope, Node Node)
{
    public string Ref => NodeRef.Qualified(Scope.Name, Node.Slug);

    /// <summary>Where the node lives, so a project node reads the same as project:slug and as name:slug.</summary>
    public string Location => Path.Combine(Scope.GraphDir, Node.Slug);

    /// <summary>How <paramref name="holder"/> writes a ref to this node: a bare slug in its own scope, qualified elsewhere.</summary>
    public string RefFrom(PlacedNode holder) => holder.Scope == Scope ? Node.Slug : Ref;
}
