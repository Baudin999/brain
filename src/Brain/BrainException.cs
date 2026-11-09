namespace Brain;

/// <summary>
/// A refusal the user can act on: the CLI prints its message to stderr and exits 1. A <see cref="Failed"/> one is no
/// refusal but a failure, such as git that would not run, which the CLI also writes to the usage log as an error.
/// </summary>
internal sealed class BrainException : Exception
{
    public BrainException()
    {
    }

    public BrainException(string message)
        : base(message)
    {
    }

    public BrainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>True for a wrong command line: the CLI adds the hint to run the command's <c>--help</c>.</summary>
    public bool IsUsage { get; private init; }

    /// <summary>True for a failure of brain or the machine it runs on, which is an error, not a refusal.</summary>
    public bool IsFailure { get; private init; }

    /// <summary>A failure of brain or the machine, with what failed and what to do in <paramref name="message"/>.</summary>
    public static BrainException Failed(string message, Exception? cause = null) =>
        cause is null ? new(message) { IsFailure = true } : new(message, cause) { IsFailure = true };

    /// <summary>A refusal for a wrong command line, with the CLI's hint to run <c>--help</c> under it.</summary>
    public static BrainException WrongCommandLine(string message) => new(message) { IsUsage = true };

    /// <summary>A wrong command line that shows the command's one-line usage.</summary>
    public static BrainException Usage(string usage) => WrongCommandLine("usage: " + usage);

    /// <summary>Throws the problem as a refusal; null is no problem.</summary>
    public static void ThrowIf(string? problem)
    {
        if (problem is not null)
        {
            throw new BrainException(problem);
        }
    }
}
