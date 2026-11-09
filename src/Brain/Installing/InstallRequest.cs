namespace Brain.Installing;

/// <summary>
/// What one run of the installer is asked for: the permission mode, when it is not to be asked, and where each part of
/// the harness links. With <see cref="Harness"/>, the <see cref="Parts"/> named, all of them when null, link there and
/// the rest to the installed harness. Without it, empty <see cref="Parts"/> link every part to the installed harness,
/// and null keeps each part linked to the checkout it links to now.
/// </summary>
internal sealed record InstallRequest(string? Mode, string? Harness, IReadOnlyList<string>? Parts);
