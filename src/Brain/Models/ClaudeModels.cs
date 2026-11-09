using System.Diagnostics;
using Brain.Children;

namespace Brain.Models;

/// <summary>
/// Asks a model through the Claude Code CLI, <c>claude -p --model &lt;model&gt; --effort &lt;effort&gt;</c>, with no
/// tools, no session kept and none of the user's settings, the prompt on stdin. It starts through <see cref="ClaudeRuns"/>, so the ask is a child
/// of this brain process and stops with it, a CLI command's or brain serve's. It fails closed: when the CLI cannot start, gives no answer within
/// the timeout or exits non-zero, it throws a refusal that says which.
/// </summary>
internal sealed class ClaudeModels(string command) : IModels
{
    public const string Command = "claude";

    public string Ask(string model, string effort, string system, string prompt, TimeSpan timeout)
    {
        using var run = Start(model, effort, system);
        var process = run.Process;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The CLI exited before it read the prompt; its exit code below says so.
        }

        if (!process.WaitForExit(timeout))
        {
            run.Stop();
            throw new BrainException($"{command} gave no answer within {timeout.TotalSeconds} seconds");
        }

        return process.ExitCode == 0
            ? output.GetAwaiter().GetResult()
            : throw new BrainException($"{command} exited {process.ExitCode}: {errors.GetAwaiter().GetResult().Trim()}");
    }

    private ClaudeRun Start(string model, string effort, string system)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var argument in Arguments(model, effort, system))
        {
            start.ArgumentList.Add(argument);
        }

        return ClaudeRuns.Start(start, model);
    }

    /// <summary>The arguments of an ask's command line: the model and effort asked, no tools, no session and no settings.</summary>
    public static IReadOnlyList<string> Arguments(string model, string effort, string system) =>
        ["-p", "--model", model, "--effort", effort, "--tools", "", "--no-session-persistence", "--setting-sources", "", "--system-prompt", system];
}
