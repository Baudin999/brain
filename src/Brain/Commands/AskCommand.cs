using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Brain.Code;
using Brain.Serving;
using Brain.Windows;

namespace Brain.Commands;

/// <summary>
/// <c>brain ask</c>: an agent in a brain code session asks the user questions, as one JSON document on stdin, which
/// brain code's window shows as questions. It checks the document first and refuses a bad one with every problem it
/// has, before anything reaches the window. The user may always answer a question in his own words, with or without an
/// option. An ask that waits blocks until the user answered every question, then prints his answers, or that he closed
/// them unanswered; one that does not prints that the questions wait and returns at once, and the server sends his
/// answers to the session as his next message once he gives them. It works only inside a brain code session, which its
/// server names to it through <see cref="CodeSession.AskVariable"/>.
/// </summary>
internal static class AskCommand
{
    public const string Name = "ask";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.Parse(arguments).Expect(0, Usage);
        if (env.CodeSession is not { } session)
        {
            throw new BrainException("brain ask works only in a session of brain code's window; outside one, ask the user in prose");
        }

        var document = AskContract.Read(env.In.ReadToEnd());
        var answer = Asked(env, new CodeQuestionsIn(session, document));
        if (answer.Answers is { } answers)
        {
            env.Out.Write(answers);
            return ExitCodes.Ok;
        }

        env.Out.WriteLine($"brain ask: your questions {answer.Id} wait for the user in brain code's window. Go on with your work: his answers come to you as his next message, whenever he gives them.");
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Hands the ask to brain code's server and waits, as long as it takes for an ask that waits, for its word; refused
    /// when no server runs, when it refuses the ask, and when it went away first.
    /// </summary>
    private static CodeQuestionsOut Asked(BrainEnv env, CodeQuestionsIn asked)
    {
        var files = new ServeFiles(env.Home, CodeServer.Folder);
        using var running = WindowClient.Running(files, CodeServer.App, env.UserHome);
        if (running is null)
        {
            throw new BrainException("brain code's window is not running, so nobody can answer; ask the user in prose");
        }

        // A body with its length: the server reads a body only by its Content-Length, which a streamed JsonContent leaves out.
        var body = JsonSerializer.Serialize(asked, CodeJson.Default.CodeQuestionsIn);
        using var waiting = new HttpClient { BaseAddress = running.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using var request = WindowClient.Keyed(HttpMethod.Post, "/api/key/ask", files);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = waiting.Send(request);
            return Answer(response);
        }
        catch (HttpRequestException gone)
        {
            throw new BrainException($"brain code's server went away before the user answered: {gone.Message}");
        }
    }

    /// <summary>The server's word; its refusal is the command's.</summary>
    private static CodeQuestionsOut Answer(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = response.Content.ReadFromJsonAsync(ReviewJson.Default.Problem).GetAwaiter().GetResult();
            throw new BrainException(problem?.Error ?? "brain code's server refused the ask");
        }

        response.EnsureSuccessStatusCode();
        var answer = response.Content.ReadFromJsonAsync(CodeJson.Default.CodeQuestionsOut).GetAwaiter().GetResult();
        return answer ?? throw BrainException.Failed("brain code's server said nothing of the ask");
    }
}
