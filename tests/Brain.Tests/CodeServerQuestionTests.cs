using System.Net;
using System.Text.Json;
using Brain.Code;

namespace Brain.Tests;

/// <summary>
/// brain ask in brain code's server: each session's claude runs with the session's id for brain ask to find it; an ask
/// that does not wait is open in its session at once and the user's answers reach claude as his next message; an ask
/// that waits gets the answers once he gave them, and a refusal once its session was closed first; a bad ask and an
/// unknown session are refused; and each question and its answer is a record in perf/code.
/// </summary>
public sealed partial class CodeServerTests
{
    private static object Asking(bool wait) => new
    {
        wait,
        questions = new[]
        {
            new { text = "Which **store**?", options = new[] { new { label = "SQLite" }, new { label = "Files" } }, recommend = "SQLite", why = "One file." },
        },
    };

    [Fact]
    public async Task AQuestionAtLeisureWaitsInItsSessionAndTheAnswersReachClaudeAsTheUsersMessage()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var asked = await Json(await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key()));
        var ask = asked.RootElement.GetProperty("id").GetString()!;
        var open = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));
        var refused = await Send(HttpMethod.Post, "/api/reply", Reply(id, ask, "SQLite", "Files"), token: token, origin: server.Origin);
        var replied = await Send(HttpMethod.Post, "/api/reply", Reply(id, ask, "Files"), token: token, origin: server.Origin);
        var after = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        Args();
        Assert.Equal(id, (await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.session"))).Trim());
        Assert.False(asked.RootElement.TryGetProperty("answers", out _));
        var question = open.RootElement.GetProperty("sessions")[0].GetProperty("questions")[0];
        Assert.Equal("open", question.GetProperty("state").GetString());
        Assert.Equal("SQLite", question.GetProperty("questions")[0].GetProperty("recommend").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("takes one choice", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, replied.StatusCode);
        Assert.Equal("answered", after.RootElement.GetProperty("sessions")[0].GetProperty("questions")[0].GetProperty("state").GetString());
        Assert.True(SpinWait.SpinUntil(() => Said().Contains("He chose: Files", StringComparison.Ordinal), Wait), Said());
        var records = Records();
        Assert.Contains("\"question\"", records, StringComparison.Ordinal);
        Assert.Contains("\"reply\"", records, StringComparison.Ordinal);
        Assert.Contains("Which **store**?", records, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerOfOnlyWordsToAQuestionWithOptionsIsAcceptedAndReachesTheAgent()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var asking = Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(true) }), key: Key());
        var ask = await OpenAsk(token);
        var replied = await Send(HttpMethod.Post, "/api/reply", Json(new { session = id, ask, replies = new[] { new { chosen = Array.Empty<string>(), words = "Neither, use a folder." } } }), token: token, origin: server.Origin);
        var answered = await Json(await asking.ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.NoContent, replied.StatusCode);
        Assert.Contains("In his own words: Neither, use a folder.", answered.RootElement.GetProperty("answers").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AQuestionThatWaitsGetsTheAnswersOnceTheUserGaveThem()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var asking = Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(true) }), key: Key());
        var ask = await OpenAsk(token);
        var replied = await Send(HttpMethod.Post, "/api/reply", Reply(id, ask, "SQLite"), token: token, origin: server.Origin);
        var answered = await Json(await asking.ConfigureAwait(true));

        Assert.Equal(HttpStatusCode.NoContent, replied.StatusCode);
        Assert.Contains("Question 1:\nWhich **store**?\nHe chose: SQLite", answered.RootElement.GetProperty("answers").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("He chose", Said(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingAQuestionThatWaitsReturnsAtOnceAndSaysTheUserClosedIt()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var asking = Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(true) }), key: Key());
        var ask = await OpenAsk(token);
        var closed = await Send(HttpMethod.Post, "/api/dismiss", Json(new { session = id, ask }), token: token, origin: server.Origin);
        var answered = await Json(await asking.ConfigureAwait(true));
        var after = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));
        var again = await Send(HttpMethod.Post, "/api/dismiss", Json(new { session = id, ask }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        var text = answered.RootElement.GetProperty("answers").GetString()!;
        Assert.StartsWith("The user closed your questions question-1 in brain code's window", text, StringComparison.Ordinal);
        Assert.Contains("without answering them. His next message says what he wants.", text, StringComparison.Ordinal);
        Assert.Equal("closed", after.RootElement.GetProperty("sessions")[0].GetProperty("questions")[0].GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("\"closed\"", Records(), StringComparison.Ordinal);
        Assert.DoesNotContain("closed your questions", Said(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingAQuestionAtLeisureSendsTheSessionTheSameLineAsAMessage()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        var asked = await Json(await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key()));
        var ask = asked.RootElement.GetProperty("id").GetString()!;
        var closed = await Send(HttpMethod.Post, "/api/dismiss", Json(new { session = id, ask }), token: token, origin: server.Origin);
        var refused = await Send(HttpMethod.Post, "/api/reply", Reply(id, ask, "Files"), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.True(SpinWait.SpinUntil(() => Said().Contains("without answering them. His next message says what he wants.", StringComparison.Ordinal), Wait), Said());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task AQuestionThatWaitsIsRefusedOnceItsSessionIsClosed()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key());
        var asking = Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(true) }), key: Key());
        await OpenAsks(token, 2);
        await Send(HttpMethod.Post, "/api/close", Json(new { session = id }), token: token, origin: server.Origin);
        var refused = await asking.ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("closed", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var unanswered = Records().Split('\n').Where((line) => line.Contains("\"unanswered\"", StringComparison.Ordinal)).ToList();
        Assert.Contains(unanswered, (line) => line.Contains("question-1", StringComparison.Ordinal));
        Assert.Contains(unanswered, (line) => line.Contains("question-2", StringComparison.Ordinal));
    }

    /// <summary>
    /// An interrupt of the turn that asks ends its open questions, as claude aborts the brain ask: the agent that waits
    /// is refused, the ask is recorded unanswered, and a later brain ask of the same session is asked as any other.
    /// </summary>
    [Fact]
    public async Task AnInterruptEndsTheOpenQuestionsAndTheSessionCanAskAgain()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "stream it");
        await ConversationWhen(token, id, (read) => read.TryGetProperty("live", out _));

        var asking = Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(true) }), key: Key());
        await OpenAsk(token);
        await Send(HttpMethod.Post, "/api/interrupt", Json(new { session = id }), token: token, origin: server.Origin);
        var refused = await asking.ConfigureAwait(true);
        await SessionWhen(token, id, (session) => !session.GetProperty("working").GetBoolean());
        var again = await Json(await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key()));
        var listed = await SessionWhen(token, id, (session) => session.GetProperty("questions").GetArrayLength() == 2);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("interrupted", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var unanswered = Records().Split('\n').Where((line) => line.Contains("\"unanswered\"", StringComparison.Ordinal)).ToList();
        Assert.Contains(unanswered, (line) => line.Contains("question-1", StringComparison.Ordinal));
        Assert.Equal("question-2", again.RootElement.GetProperty("id").GetString());
        var states = listed.GetProperty("questions").EnumerateArray().Select((one) => one.GetProperty("state").GetString()).ToList();
        Assert.Equal(["ended", "open"], states);
    }

    [Fact]
    public async Task ABadAskOrAnUnknownSessionIsRefused()
    {
        var id = await StartSession();

        var bad = await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = new { wait = true, questions = new[] { new { text = "Which?", recommend = "A" } } } }), key: Key());
        var unknown = await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = Guid.NewGuid().ToString("D"), ask = Asking(false) }), key: Key());
        var keyless = await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }));

        Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
        Assert.Contains("question 1 has no why", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, keyless.StatusCode);
    }

    /// <summary>
    /// The window in a real browser: a question a session asks shows in its conversation with the recommended option
    /// marked, in the session's open questions and on the page of every open question; a choice and Answer answer it.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsAQuestionAndTheUserAnswersIt()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            var id = await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key()).ConfigureAwait(false);

            await window.Until("document.querySelector('#conversation .question .question-text .prose strong')?.textContent", "store").ConfigureAwait(false);
            await window.Until("document.querySelector('.question-choice-mark')?.closest('.question-choice').querySelector('.question-choice-label').textContent", "SQLite").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session-state').dataset.state", "asking").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.code-open-question').length", "1").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-questions-row').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.code-asked .question').length", "1").ConfigureAwait(false);

            await window.Eval("document.querySelectorAll('.code-asked .question-choice')[1].click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-asked .question .button.yes').disabled", "false").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-asked .question .button.yes').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-none')?.textContent", "No question waits for you.").ConfigureAwait(false);
            Assert.True(SpinWait.SpinUntil(() => Said().Contains("He chose: Files", StringComparison.Ordinal), Wait), Said());

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The window in a real browser: a question that takes words draws an input box of its own, and Close shuts an open
    /// ask unanswered: it leaves the open questions, and a question with options draws the box too.
    /// </summary>
    [WindowFact]
    public async Task TheWindowDrawsTheWordsBoxAndTheUserClosesAnAskUnanswered()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            var id = await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            var words = new { wait = false, questions = new[] { new { text = "What should it say?", recommend = "Nothing", why = "It is true." } } };
            await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = words }), key: Key()).ConfigureAwait(false);

            await window.Until("document.querySelector('#conversation .question-words')?.getBoundingClientRect().height > 20", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/ask", Json(new { session = id, ask = Asking(false) }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelectorAll('#conversation .question-words').length", "2").ConfigureAwait(false);
            await window.Eval("[...document.querySelectorAll('#conversation .question .button')].find((one) => one.textContent === 'Close').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('#conversation .question')?.dataset.state", "closed").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.code-open-question').length", "1").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-words')?.disabled", "false").ConfigureAwait(false);
            Assert.True(SpinWait.SpinUntil(() => Said().Contains("without answering them", StringComparison.Ordinal), Wait), Said());

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The window's answer to the one question of the ask <paramref name="ask"/>: the options it chose.</summary>
    private static string Reply(string session, string ask, params string[] chosen) =>
        Json(new { session, ask, replies = new[] { new { chosen, words = string.Empty } } });

    /// <summary>Starts a session of the open agent in the project with the key, as brain code does, and gives its id.</summary>
    private async Task<string> StartSession()
    {
        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()).ConfigureAwait(false)).ConfigureAwait(false);
        return started.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The id of the first open ask of the first session, once there is one; it fails after a wait.</summary>
    private Task<string> OpenAsk(string token) => OpenAsks(token, 1);

    /// <summary>The id of the first ask of the first session, once it asked <paramref name="count"/>; it fails after a wait.</summary>
    private async Task<string> OpenAsks(string token, int count)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            var listed = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            var questions = listed.RootElement.GetProperty("sessions")[0].GetProperty("questions");
            if (questions.GetArrayLength() >= count)
            {
                return questions[0].GetProperty("id").GetString()!;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException("no question was asked");
    }

    /// <summary>Every line the stand-in for claude was sent so far.</summary>
    private string Said()
    {
        var path = Path.Combine(brain.Root, "claude.said");
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return File.ReadAllText(path);
    }

    /// <summary>Everything brain code's server recorded in perf/code.</summary>
    private string Records()
    {
        var folder = Path.Combine(brain.Home, "perf", CodeServer.Folder);
        return string.Join('\n', Directory.EnumerateFiles(folder).Select(File.ReadAllText));
    }
}
