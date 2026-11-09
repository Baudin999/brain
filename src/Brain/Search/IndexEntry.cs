namespace Brain.Search;

/// <summary>A vector row of the search index: a node's key, its scope's key, the hash of the passage embedded, and the vector.</summary>
internal sealed record IndexEntry(string Key, string Scope, string Hash, float[] Vector);

/// <summary>A full-text row of the search index: a node's key, its scope's key, and the hash of its columns.</summary>
internal sealed record IndexRow(string Key, string Scope, string Hash);
