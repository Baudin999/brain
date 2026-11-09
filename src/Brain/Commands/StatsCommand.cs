using System.Text.Json;
using Brain.Stats;

namespace Brain.Commands;

/// <summary>
/// <c>brain stats</c>: runs every metric in <see cref="Metrics.All"/> over the usage log in the range asked and prints
/// their sections, each section's evidence first with every case it counted. Every token count is an estimate,
/// characters divided by four, and says so.
/// </summary>
internal static class StatsCommand
{
    public const string Name = "stats";
    private const string SinceOption = "since";
    private const string UntilOption = "until";
    private const string JsonFlag = "json";

    // navarone: 0000151
    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [JsonFlag], SinceOption, UntilOption);
        options.Expect(0, Help.UsageOf(Name));
        var report = Metrics.Measure(env.Home, Day(options.Get(SinceOption), SinceOption), Day(options.Get(UntilOption), UntilOption));
        if (options.Has(JsonFlag))
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, StatsJson.Default.StatsReport));
            return ExitCodes.Ok;
        }

        env.Out.WriteLine(report.Heading);
        if (report.Unreadable > 0)
        {
            env.Out.WriteLine($"{Figures.Number(report.Unreadable)} lines of the usage log could not be read and are left out.");
        }

        foreach (var section in report.Sections)
        {
            env.Out.WriteLine();
            Print(env, section);
        }

        return ExitCodes.Ok;
    }

    private static void Print(BrainEnv env, MetricSection section)
    {
        env.Out.WriteLine(section.Title);
        if (section.Story is { } story)
        {
            env.Out.WriteLine("  " + story);
        }

        if (section.Note is { } note)
        {
            env.Out.WriteLine("  " + note);
        }

        foreach (var proof in section.Cases ?? [])
        {
            env.Out.WriteLine($"    - {proof.Subject}: {proof.Detail}");
        }

        foreach (var line in section.Lines)
        {
            env.Out.WriteLine(line);
        }
    }

    private static DateOnly? Day(string? text, string option) =>
        text is null ? null : Figures.Day(text) ?? throw BrainException.WrongCommandLine($"--{option} takes a day as {FieldNames.DateFormat}: {text}");
}
