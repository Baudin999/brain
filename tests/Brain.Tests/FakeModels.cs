using Brain.Dreaming;
using Brain.Models;

namespace Brain.Tests;

/// <summary>
/// Stands in for the Claude models, so the gate never calls one: each cheap dreamer, or the judge, answers from
/// <see cref="Drafts"/> by its model, the comparison <see cref="Comparison"/> and the escalation <see cref="Escalation"/>. It
/// records every call, and a model with no answer set gives none.
/// </summary>
internal sealed class FakeModels : IModels
{
    private readonly Lock calls = new();

    public Dictionary<string, string> Drafts { get; } = new(StringComparer.Ordinal);

    public string? Comparison { get; set; }

    public string? Escalation { get; set; }

    /// <summary>Runs while a model answers, as a sitting starting at that moment would.</summary>
    public Action? During { get; set; }

    public List<(string Model, string Effort, string System, string Prompt)> Calls { get; } = [];

    public string Ask(string model, string effort, string system, string prompt, TimeSpan timeout)
    {
        lock (calls)
        {
            Calls.Add((model, effort, system, prompt));
        }

        During?.Invoke();
        var answer = system == DreamPrompt.Comparison ? Comparison
            : system == DreamPrompt.Escalation ? Escalation
            : Drafts.GetValueOrDefault(model);
        return answer ?? throw new BrainException($"{model} has no answer in this test");
    }
}
