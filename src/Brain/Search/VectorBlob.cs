using System.Runtime.InteropServices;

namespace Brain.Search;

/// <summary>A vector as the bytes SQLite stores: its floats in machine order.</summary>
internal static class VectorBlob
{
    public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    public static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
}
