using Brain.Models;

namespace Brain.Judging;

/// <summary>
/// Asks the model and effort the judge setting in <paramref name="home"/>'s settings.json names, through
/// <paramref name="models"/>, with the rules read from the brain skill at every call. It fails closed: when the rules
/// are missing, the settings cannot be read, the model gives no answer or answers something that is not a verdict, it
/// throws a refusal saying the judge is unreachable.
/// </summary>
internal sealed class ClaudeJudge(string rulesPath, IModels models, string home) : IJudge
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public Verdict Judge(IReadOnlyList<JudgedEntry> entries)
    {
        var rules = (File.Exists(rulesPath) ? WritingRules.From(File.ReadAllText(rulesPath)) : null)
            ?? throw Unreachable($"{rulesPath} does not hold the {WritingRules.Count} writing rules");
        string answer;
        try
        {
            var (model, effort) = BrainSettings.Read(home).Judge;
            answer = models.Ask(model, effort, JudgePrompt.System, JudgePrompt.Build(rules, entries), Timeout);
        }
        catch (BrainException silent)
        {
            throw Unreachable(silent.Message);
        }

        return Verdict.Parse(answer) ?? throw Unreachable($"its answer is not a verdict: {answer.Trim()}");
    }

    private static BrainException Unreachable(string why) => new($"the judge is unreachable: {why}");
}
