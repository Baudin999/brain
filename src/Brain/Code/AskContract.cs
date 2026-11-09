using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Brain.Code;

/// <summary>
/// What <c>brain ask</c> reads on stdin: whether the agent waits for the answers, and its questions. A field brain does
/// not know is refused, so a misspelt one never passes as one left out.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AskDocument(bool? Wait, IReadOnlyList<AskQuestion>? Questions);

/// <summary>
/// One question: its markdown text, the options to choose from, if any, the agent's recommendation and why, whether
/// more than one option may be chosen, and own, which older asks set and which changes nothing: the user may always
/// answer in his own words, beside an option or alone. With options, the recommendation is one option's label.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AskQuestion(
    string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AskOption>? Options,
    string? Recommend,
    string? Why,
    bool Multiple = false,
    bool Own = false);

/// <summary>One option of a question: its label and what it means.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AskOption(string? Label, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null);

/// <summary>The user's answer to one question: the labels he chose and his own words.</summary>
internal sealed record AskReply(IReadOnlyList<string>? Chosen, string? Words);

/// <summary>
/// The questions of one <c>brain ask</c>, as the window draws them: their id in the session, whether the agent waits,
/// how many of the session's events were written when they were asked, so the window draws them after those, when they
/// were asked, the questions, whether they are open, answered or ended, and, once answered, the user's answers.
/// </summary>
internal sealed record CodeQuestions(
    string Id,
    bool Wait,
    int At,
    DateTimeOffset Asked,
    IReadOnlyList<AskQuestion> Questions,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<AskReply>? Replies = null)
{
    public const string Open = "open";
    public const string Answered = "answered";

    /// <summary>The user closed the questions without answering them.</summary>
    public const string Closed = "closed";

    /// <summary>The session ended before the user answered.</summary>
    public const string Ended = "ended";
}

/// <summary>A <c>brain ask</c> handed to brain code's server: the session that asks, and what it asks.</summary>
internal sealed record CodeQuestionsIn(string? Session, AskDocument? Ask);

/// <summary>The server's word to <c>brain ask</c>: the questions' id, and, for an agent that waits, the user's answers.</summary>
internal sealed record CodeQuestionsOut(string Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Answers);

/// <summary>The user's answers to the questions <c>Ask</c> of a session: one for each question, in their order.</summary>
internal sealed record CodeReply(string? Session, string? Ask, IReadOnlyList<AskReply>? Replies);

/// <summary>The user's word that he closes the questions <c>Ask</c> of a session without answering them.</summary>
internal sealed record CodeDismiss(string? Session, string? Ask);

/// <summary>
/// The contract of <c>brain ask</c>, held in one place for the command and the server alike: what a document must say
/// before it reaches the window, what an answer must say before it reaches the agent, and the text the agent reads.
/// </summary>
// navarone: 0000185
internal static class AskContract
{
    /// <summary>The document on <paramref name="json"/>; refused, with every problem it has, when it is no good ask.</summary>
    public static AskDocument Read(string json)
    {
        AskDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, CodeJson.Default.AskDocument);
        }
        catch (JsonException error)
        {
            throw new BrainException($"brain ask reads no ask from this: {error.Message}");
        }

        return Checked(document);
    }

    /// <summary><paramref name="document"/> when it is a good ask; refused, with every problem it has, when not.</summary>
    // navarone: 0000186
    public static AskDocument Checked(AskDocument? document)
    {
        var problems = Problems(document);
        if (document is null || problems.Count > 0)
        {
            throw new BrainException("brain ask refused the ask:\n" + string.Join('\n', problems));
        }

        return document;
    }

    /// <summary>What is wrong with <paramref name="document"/>, one line a problem; none for a good ask.</summary>
    public static IReadOnlyList<string> Problems(AskDocument? document)
    {
        if (document is null)
        {
            return ["the ask is empty: send one JSON object on stdin"];
        }

        var problems = new List<string>();
        if (document.Wait is null)
        {
            problems.Add("wait is missing: true when you cannot go on without the answers, false to go on and have them sent to you as a message");
        }

        if (document.Questions is not { Count: > 0 } questions)
        {
            problems.Add("questions is missing: ask at least one");
            return problems;
        }

        for (var at = 0; at < questions.Count; at++)
        {
            problems.AddRange(QuestionProblems($"question {at + 1}", questions[at]));
        }

        return problems;
    }

    /// <summary>
    /// What is wrong with <paramref name="replies"/> as the answers to <paramref name="questions"/>, or null when each
    /// question has its answer: an option of its own, one unless it takes more, or words, which every question takes.
    /// </summary>
    // navarone: 0000212
    public static string? ReplyProblem(IReadOnlyList<AskQuestion> questions, IReadOnlyList<AskReply> replies)
    {
        if (replies.Count != questions.Count)
        {
            return "answer every question";
        }

        for (var at = 0; at < questions.Count; at++)
        {
            if (AnswerProblem($"question {at + 1}", questions[at], replies[at]) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// The text the agent reads when the user closed the questions <paramref name="asked"/> without answering them.
    /// </summary>
    public static string Closed(CodeQuestions asked)
    {
        return $"The user closed your questions {asked.Id} in brain code's window without answering them. His next message says what he wants.\n";
    }

    /// <summary>The text the agent reads of the user's answers: each question, what he chose and his own words.</summary>
    public static string Answers(CodeQuestions asked, IReadOnlyList<AskReply> replies)
    {
        var text = new StringBuilder();
        if (asked.Wait)
        {
            text.Append(CultureInfo.InvariantCulture, $"The user answered your questions {asked.Id} in brain code's window.\n");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"The user answered the questions {asked.Id} you asked with brain ask at his leisure, in brain code's window.\n");
        }

        for (var at = 0; at < asked.Questions.Count; at++)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nQuestion {at + 1}:\n{asked.Questions[at].Text}\n");
            var reply = replies[at];
            if (reply.Chosen is { Count: > 0 } chosen)
            {
                text.Append(CultureInfo.InvariantCulture, $"He chose: {string.Join(", ", chosen)}\n");
            }

            if (!string.IsNullOrWhiteSpace(reply.Words))
            {
                text.Append(CultureInfo.InvariantCulture, $"In his own words: {reply.Words.Trim()}\n");
            }
        }

        return text.ToString();
    }

    private static List<string> QuestionProblems(string name, AskQuestion question)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(question.Text))
        {
            problems.Add($"{name} has no text");
        }

        if (string.IsNullOrWhiteSpace(question.Recommend))
        {
            problems.Add($"{name} has no recommend: say what you would answer");
        }

        if (string.IsNullOrWhiteSpace(question.Why))
        {
            problems.Add($"{name} has no why: say why you recommend it");
        }

        if (question.Options is { } options)
        {
            problems.AddRange(OptionProblems(name, options, question.Recommend));
            return problems;
        }

        if (question.Multiple)
        {
            problems.Add($"{name} is multiple but has no options to choose from");
        }

        return problems;
    }

    private static List<string> OptionProblems(string name, IReadOnlyList<AskOption> options, string? recommend)
    {
        if (options.Count == 0)
        {
            return [$"{name} has an empty options list: leave options out for an answer in his own words"];
        }

        var problems = new List<string>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 0; at < options.Count; at++)
        {
            var label = options[at].Label;
            if (string.IsNullOrWhiteSpace(label))
            {
                problems.Add($"{name}, option {at + 1} has no label");
                continue;
            }

            if (!labels.Add(label))
            {
                problems.Add($"{name} has two options labelled {label}");
            }
        }

        if (!string.IsNullOrWhiteSpace(recommend) && !labels.Contains(recommend))
        {
            problems.Add($"{name} recommends {recommend}, which is no option's label");
        }

        return problems;
    }

    // navarone: 0000184
    private static string? AnswerProblem(string name, AskQuestion question, AskReply reply)
    {
        var chosen = reply.Chosen ?? [];
        var hasWords = !string.IsNullOrWhiteSpace(reply.Words);
        var labels = (question.Options ?? []).Select((option) => option.Label).ToHashSet(StringComparer.Ordinal);
        if (chosen.Count == 0 && !hasWords)
        {
            return $"{name} has no answer yet";
        }

        var stranger = chosen.FirstOrDefault((label) => !labels.Contains(label));
        if (stranger is not null)
        {
            return $"{stranger} is no option of {name}";
        }

        if (chosen.Count > 1 && !question.Multiple)
        {
            return $"{name} takes one choice";
        }

        return null;
    }
}
