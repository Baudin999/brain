using System.Text.Json.Serialization;

namespace Brain.Windows;

/// <summary>What a small window's server says to the command that started it: which window it is, and over which folder.</summary>
internal sealed record WindowHello(string App, string Root);

/// <summary>A window's launch code, which it trades once for its session.</summary>
internal sealed record WindowLaunch(string? Code);

/// <summary>A window's session, and what it shows: its folder's path, its name and the title the window takes.</summary>
internal sealed record WindowSession(string Token, string Root, string Name, string Title);

/// <summary>How the window came forward: raised by the compositor, opened afresh, or not at all for want of a browser.</summary>
internal sealed record WindowRaised(string Window);

/// <summary>A batch of the window's command records.</summary>
internal sealed record RecordBatch(IReadOnlyList<WindowRecord>? Records);

/// <summary>The server's records since the one the window saw last, and the number of the last.</summary>
internal sealed record ServerRecords(long Last, IReadOnlyList<WindowRecord> Records);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WindowHello))]
[JsonSerializable(typeof(WindowLaunch))]
[JsonSerializable(typeof(WindowSession))]
[JsonSerializable(typeof(WindowRaised))]
[JsonSerializable(typeof(RecordBatch))]
[JsonSerializable(typeof(ServerRecords))]
[JsonSerializable(typeof(WindowRecord))]
internal sealed partial class WindowJson : JsonSerializerContext;
