using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Children;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The built brain binary in its own process, with stand-ins for claude on its PATH that start a process in the
/// background and one detached from it, in a session of its own and orphaned. brain serve runs a chat's agent and a
/// sitting's; <c>brain add</c> asks the judge. However that brain process stops, a clean stop on a terminate, or a kill
/// that leaves it no moment to clean up, as a crash leaves none, nothing a claude it started started outlives it.
/// </summary>
public sealed partial class ServeStopTests : IDisposable
{
    private static readonly TimeSpan Waiting = TimeSpan.FromSeconds(15);

    private readonly TempBrain brain = new();
    private Process? running;

    public void Dispose()
    {
        if (running is { HasExited: false })
        {
            running.Kill();
            running.WaitForExit();
        }

        running?.Dispose();
        brain.Dispose();
    }

    [Theory]
    [InlineData(Posix.SigKill)]
    [InlineData(Posix.SigTerm)]
    public async Task NothingAChatOrASittingStartedOutlivesBrainServe(int signal)
    {
        var port = FreePort();
        Run(["serve"], port);
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)) };
        var origin = client.BaseAddress.ToString().TrimEnd('/');
        var token = await Trade(client, origin, LaunchCode());
        await Task.Delay(WindowKeys.Arming + TimeSpan.FromMilliseconds(200));
        using var chat = await Post(client, origin, token, "/api/chat/start", new { tab = ":home", place = "Home" });
        (await Post(client, origin, token, "/api/chat/say", new { id = chat.RootElement.GetProperty("id").GetString(), text = "Start a build." })).Dispose();
        using var conversation = await Post(client, origin, token, "/api/conversation/start", new { scope = "global", model = "haiku", effort = "low" });
        var reference = conversation.RootElement.GetProperty("row").GetProperty("ref").GetString();
        (await Post(client, origin, token, "/api/conversation/say", new { @ref = reference, text = "Start a build." })).Dispose();
        var started = Started(4);

        Posix.Signal(running!.Id, signal);
        using var waiting = new CancellationTokenSource(Waiting);
        await running.WaitForExitAsync(waiting.Token);

        Assert.All(started, pid => Assert.True(ReviewServerTests.Gone(pid), $"process {pid} outlived brain serve"));
    }

    /// <summary>A CLI command that asks a model, as <c>brain add</c> asks the judge, makes the ask its child, which stops with it.</summary>
    [Fact]
    public async Task NothingAJudgeStartedOutlivesTheCommandThatAskedIt()
    {
        var rules = Path.Combine(brain.Root, "claude-config", "skills", "brain", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(rules)!);
        await using (var skill = typeof(ClaudeRuns).Assembly.GetManifestResourceStream("harness/skills/brain/SKILL.md")!)
        await using (var file = File.Create(rules))
        {
            await skill.CopyToAsync(file);
        }

        Run(["add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"], FreePort(), judge: true);
        var started = Started(2);

        running!.Kill();
        using var waiting = new CancellationTokenSource(Waiting);
        await running.WaitForExitAsync(waiting.Token);

        Assert.All(started, pid => Assert.True(ReviewServerTests.Gone(pid), $"process {pid} outlived brain add"));
    }

    /// <summary>
    /// Runs the built brain with <paramref name="arguments"/>, with a stand-in for claude and the browser on its PATH.
    /// The stand-in answers the open agent's lines, or, asked once with the prompt on stdin as the judge asks, waits.
    /// </summary>
    private void Run(IReadOnlyList<string> arguments, int port, bool judge = false)
    {
        var bin = Path.Combine(brain.Root, "bin");
        Directory.CreateDirectory(bin);
        Script(Path.Combine(bin, "claude"), $$"""
            #!/bin/sh
            here={{brain.Root}}
            start() {
              sleep 300 </dev/null >/dev/null 2>&1 &
              echo $! >> "$here/started"
              ( setsid sleep 300 </dev/null >/dev/null 2>&1 & echo $! >> "$here/started" )
            }
            case " $* " in
              *" --agent "*)
                while IFS= read -r line; do
                  start
                  echo '{"type":"result","subtype":"success","is_error":false,"result":"ok"}'
                done
                ;;
              *)
                cat >/dev/null
                start
                sleep 300
                ;;
            esac
            """);
        var browser = Path.Combine(bin, "browser");
        Script(browser, $$"""
            #!/bin/sh
            printf '%s\n' "$@" > "{{brain.Root}}/browser.args"
            """);
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "brain"))
        {
            UseShellExecute = false,
            WorkingDirectory = brain.Home,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["BRAIN_HOME"] = brain.Home;
        info.Environment["BRAIN_TRANSCRIPTS"] = brain.Transcripts;
        info.Environment["BRAIN_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        info.Environment["BRAIN_BROWSER"] = browser;
        info.Environment["HOME"] = brain.Root;
        info.Environment["XDG_DATA_HOME"] = Path.Combine(brain.Root, "data");
        info.Environment["CLAUDE_CONFIG_DIR"] = Path.Combine(brain.Root, "claude-config");
        info.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        info.Environment.Remove("CLAUDE_CODE_SESSION_ID");
        if (judge)
        {
            info.Environment.Remove("BRAIN_JUDGE");
        }
        else
        {
            info.Environment["BRAIN_JUDGE"] = "off";
        }

        running = Process.Start(info)!;
        running.OutputDataReceived += (_, _) => { };
        running.ErrorDataReceived += (_, _) => { };
        running.BeginOutputReadLine();
        running.BeginErrorReadLine();
    }

    /// <summary>The launch code in the launch file brain serve opened its window on.</summary>
    private string LaunchCode()
    {
        var args = Path.Combine(brain.Root, "browser.args");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(args) && File.ReadAllText(args).Contains("--app=", StringComparison.Ordinal), Waiting), "brain serve opened no window");
        var url = File.ReadAllLines(args).First(line => line.StartsWith("--app=", StringComparison.Ordinal))["--app=".Length..];
        return Code().Match(File.ReadAllText(new Uri(url).LocalPath)).Groups[1].Value;
    }

    /// <summary>The pids of the processes the stand-ins started, once they started <paramref name="count"/>.</summary>
    private List<int> Started(int count)
    {
        var path = Path.Combine(brain.Root, "started");
        List<int> Read() => File.Exists(path) ? [.. File.ReadAllLines(path).Where(line => line.Length > 0).Select(line => int.Parse(line, CultureInfo.InvariantCulture))] : [];
        Assert.True(SpinWait.SpinUntil(() => Read().Count >= count, Waiting), "the stand-ins for claude started too little");
        return Read();
    }

    private static async Task<string> Trade(HttpClient client, string origin, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/launch") { Content = Body(new { code }) };
        request.Headers.Add("Origin", origin);
        var reply = await client.SendAsync(request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<JsonDocument> Post(HttpClient client, string origin, string token, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = Body(body) };
        request.Headers.Add("Origin", origin);
        request.Headers.Add(ReviewServer.SessionHeader, token);
        var reply = await client.SendAsync(request).ConfigureAwait(false);
        var said = await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.True(reply.StatusCode == HttpStatusCode.OK, said);
        return JsonDocument.Parse(said);
    }

    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static void Script(string path, string text)
    {
        File.WriteAllText(path, text);
        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [GeneratedRegex("#launch=([0-9a-f]{64})")]
    private static partial Regex Code();
}
