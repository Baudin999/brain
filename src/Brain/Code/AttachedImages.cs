using Brain.Children;

namespace Brain.Code;

/// <summary>
/// The images the user attaches to his words in brain code's window, by a drop, a paste or the file explorer: each one
/// of the media types Claude reads, in base64, opening with that type's first bytes, and at most
/// <see cref="Largest"/> bytes, so claude is never sent an image it refuses.
/// </summary>
internal static class AttachedImages
{
    /// <summary>The most bytes one image may hold.</summary>
    public const int Largest = 5 * 1024 * 1024;

    private const double Megabyte = 1024 * 1024;

    /// <summary>The media type of each extension an image file has.</summary>
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    /// <summary>The media type of the image file <paramref name="path"/> by its extension, or null for a file that is no image.</summary>
    public static string? MediaTypeOf(string path)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(path), out var type))
        {
            return null;
        }

        return type;
    }

    /// <summary>
    /// The marks each media type's file opens with, each a run of bytes at its place: a GIF has two, and a WebP its size
    /// between RIFF and WEBP.
    /// </summary>
    private static readonly Signature[] Signatures =
    [
        new("image/png", [new(0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])]),
        new("image/jpeg", [new(0, [0xFF, 0xD8, 0xFF])]),
        new("image/gif", [new(0, "GIF87a"u8.ToArray())]),
        new("image/gif", [new(0, "GIF89a"u8.ToArray())]),
        new("image/webp", [new(0, "RIFF"u8.ToArray()), new(8, "WEBP"u8.ToArray())]),
    ];

    /// <summary>The media type <paramref name="bytes"/> open as, by their first bytes, or null for bytes that open as no image's.</summary>
    public static string? Sniffed(ReadOnlySpan<byte> bytes)
    {
        foreach (var signature in Signatures)
        {
            if (Opens(bytes, signature.Marks))
            {
                return signature.MediaType;
            }
        }

        return null;
    }

    private static bool Opens(ReadOnlySpan<byte> bytes, IReadOnlyList<Mark> marks)
    {
        foreach (var mark in marks)
        {
            if (bytes.Length < mark.At + mark.Bytes.Length)
            {
                return false;
            }

            if (!bytes.Slice(mark.At, mark.Bytes.Length).SequenceEqual(mark.Bytes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A media type and the marks its file opens with.</summary>
    private sealed record Signature(string MediaType, IReadOnlyList<Mark> Marks);

    /// <summary>A run of bytes a file holds at the place <c>At</c>.</summary>
    private sealed record Mark(int At, byte[] Bytes);

    /// <summary>
    /// The images as a message carries them; refused, with why, for one of another type, not in base64, too large, or
    /// whose bytes are not its type's.
    /// </summary>
    // navarone: 0000209
    public static IReadOnlyList<ImageBlock> Checked(IReadOnlyList<CodeImage>? images)
    {
        if (images is null)
        {
            return [];
        }

        var blocks = new List<ImageBlock>();
        foreach (var image in images)
        {
            blocks.Add(Checked(image));
        }

        return blocks;
    }

    private static ImageBlock Checked(CodeImage image)
    {
        var name = image.Name ?? "an image";
        var type = image.MediaType ?? string.Empty;
        if (!ByExtension.ContainsValue(type))
        {
            throw new BrainException($"{name} is no png, jpeg, gif or webp image");
        }

        var data = image.Data ?? string.Empty;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            throw new BrainException($"{name} came without its bytes in base64");
        }

        if (bytes.Length == 0)
        {
            throw new BrainException($"{name} is empty");
        }

        if (bytes.Length > Largest)
        {
            throw new BrainException($"{name} is {bytes.Length / Megabyte:0.0} MB, and an image goes up to {Largest / Megabyte:0} MB");
        }

        if (Sniffed(bytes) != type)
        {
            throw new BrainException($"{name} is no {type} image");
        }

        return new ImageBlock(type, data);
    }
}
