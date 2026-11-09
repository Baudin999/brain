using Brain.Code;
using Brain.Commands;

namespace Brain.Tests;

/// <summary>
/// The contract of <c>brain ask</c>: a document is refused with every problem it has, a misspelt field among them,
/// before anything reaches the window; an answer must answer each question as it takes one; the agent reads each
/// question with what the user chose and said; and the command works only inside a brain code session.
/// </summary>
public sealed class AskTests : IDisposable
{
    private const string Good = """{"wait": true, "questions": [{"text": "Which **store**?", "options": [{"label": "SQLite", "description": "One file"}, {"label": "Files"}], "recommend": "SQLite", "why": "One file to back up."}]}""";

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AGoodAskIsReadAsItsQuestions()
    {
        var document = AskContract.Read(Good);

        Assert.True(document.Wait);
        var question = Assert.Single(document.Questions!);
        Assert.Equal(["SQLite", "Files"], question.Options!.Select((option) => option.Label));
        Assert.Equal("One file", question.Options![0].Description);
        Assert.False(question.Multiple);
    }

    [Theory]
    [InlineData("""{"questions": [{"text": "a", "recommend": "b", "why": "c"}]}""", "wait is missing")]
    [InlineData("""{"wait": false}""", "questions is missing")]
    [InlineData("""{"wait": false, "questions": []}""", "questions is missing")]
    [InlineData("""{"wait": false, "questions": [{"recommend": "b", "why": "c"}]}""", "question 1 has no text")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "why": "c"}]}""", "question 1 has no recommend")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "b"}]}""", "question 1 has no why")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "b", "why": "c", "options": []}]}""", "empty options list")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "b", "why": "c", "options": [{"label": "x"}]}]}""", "recommends b, which is no option's label")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "x", "why": "c", "options": [{"label": "x"}, {"label": "x"}]}]}""", "two options labelled x")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "x", "why": "c", "options": [{"description": "d"}, {"label": "x"}]}]}""", "question 1, option 1 has no label")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "b", "why": "c", "multiple": true}]}""", "multiple but has no options")]
    [InlineData("""{"wait": false, "questions": [{"text": "a", "recommend": "b", "why": "c", "recommendation": "b"}]}""", "recommendation")]
    [InlineData("""{"wait": "yes", "questions": []}""", "reads no ask")]
    [InlineData("not json", "reads no ask")]
    [InlineData("", "reads no ask")]
    public void ABadAskIsRefusedWithWhatIsWrong(string json, string problem)
    {
        var refused = Assert.Throws<BrainException>(() => AskContract.Read(json));

        Assert.Contains(problem, refused.Message, StringComparison.Ordinal);
        Assert.False(refused.IsFailure);
    }

    [Fact]
    public void EveryProblemOfAnAskIsSaidAtOnce()
    {
        var refused = Assert.Throws<BrainException>(() => AskContract.Read("""{"questions": [{"text": "a"}, {"recommend": "b", "why": "c"}]}"""));

        Assert.Contains("wait is missing", refused.Message, StringComparison.Ordinal);
        Assert.Contains("question 1 has no recommend", refused.Message, StringComparison.Ordinal);
        Assert.Contains("question 2 has no text", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, "question 1 has no answer yet")]
    [InlineData("Neither", null, "Neither is no option of question 1")]
    [InlineData("SQLite,Files", null, "question 1 takes one choice")]
    [InlineData(null, "my words", null)]
    [InlineData("SQLite", "and more", null)]
    [InlineData("SQLite", null, null)]
    public void AnAnswerMustAnswerTheQuestionAsItTakesOne(string? chosen, string? words, string? problem)
    {
        var questions = AskContract.Read(Good).Questions!;
        var picked = chosen?.Split(',') ?? [];

        Assert.Equal(problem, AskContract.ReplyProblem(questions, [new AskReply(picked, words)]));
        Assert.Equal("answer every question", AskContract.ReplyProblem(questions, []));
    }

    [Fact]
    public void EveryQuestionTakesWordsWithOrWithoutOptionsAndOwnChangesNothing()
    {
        var questions = AskContract.Read("""{"wait": false, "questions": [{"text": "a", "recommend": "b", "why": "c"}, {"text": "d", "options": [{"label": "x"}, {"label": "y"}], "recommend": "x", "why": "e", "multiple": true, "own": true}, {"text": "f", "options": [{"label": "p"}], "recommend": "p", "why": "g"}]}""").Questions!;

        Assert.Null(AskContract.ReplyProblem(questions, [new AskReply(null, "b it is"), new AskReply(["x", "y"], "and z"), new AskReply([], "only words")]));
    }

    [Fact]
    public void TheAgentReadsEachQuestionWithWhatTheUserChoseAndSaid()
    {
        var document = AskContract.Read(Good);
        var asked = new CodeQuestions("question-1", true, 0, DateTimeOffset.UnixEpoch, document.Questions!, CodeQuestions.Answered);

        var text = AskContract.Answers(asked, [new AskReply(["Files"], " Grep is all I use. ")]);

        Assert.Contains("answered your questions question-1", text, StringComparison.Ordinal);
        Assert.Contains("Question 1:\nWhich **store**?\nHe chose: Files\nIn his own words: Grep is all I use.\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosedAskTellsTheAgentTheUserClosedItAndHisNextMessageSaysWhatHeWants()
    {
        var asked = new CodeQuestions("question-3", Wait: true, At: 0, DateTimeOffset.UnixEpoch, AskContract.Read(Good).Questions!, CodeQuestions.Closed);

        var text = AskContract.Closed(asked);

        Assert.Equal("The user closed your questions question-3 in brain code's window without answering them. His next message says what he wants.\n", text);
    }

    [Fact]
    public void AnAskOutsideABrainCodeSessionIsRefusedAndSaysToAskInProse()
    {
        var env = brain.Env(new StringReader(Good), TextWriter.Null, TextWriter.Null);

        var refused = Assert.Throws<BrainException>(() => AskCommand.Run(env, []));

        Assert.Contains("ask the user in prose", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadAskInASessionIsRefusedBeforeTheWindowIsLookedFor()
    {
        var env = brain.Env(new StringReader("""{"wait": true}"""), TextWriter.Null, TextWriter.Null) with { CodeSession = Guid.NewGuid().ToString("D") };

        var refused = Assert.Throws<BrainException>(() => AskCommand.Run(env, []));

        Assert.Contains("questions is missing", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGoodAskWithNoWindowRunningIsRefused()
    {
        var env = brain.Env(new StringReader(Good), TextWriter.Null, TextWriter.Null) with { CodeSession = Guid.NewGuid().ToString("D") };

        var refused = Assert.Throws<BrainException>(() => AskCommand.Run(env, []));

        Assert.Contains("brain code's window is not running", refused.Message, StringComparison.Ordinal);
    }
}
