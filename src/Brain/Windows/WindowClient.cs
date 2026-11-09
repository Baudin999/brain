using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Brain.Serving;

namespace Brain.Windows;

/// <summary>
/// The side of a small window's command that talks to its server, as <c>brain workspace</c> and <c>brain code</c> do: it
/// finds the server that runs by the port and key in its <see cref="ServeFiles"/>, raises its window, waits for a new one
/// to answer, and opens a window in the browser.
/// </summary>
internal static class WindowClient
{
    private static readonly TimeSpan Ask = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    /// <summary>A client of the server that runs, when it is <paramref name="app"/> over <paramref name="root"/> and answers to its key; else null.</summary>
    // navarone: 0000168
    public static HttpClient? Running(ServeFiles files, string app, string root)
    {
        if (files.Read(WindowServer.PortFile) is not { } port || files.ReadKey() is null)
        {
            return null;
        }

        var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = Ask };
        if (Hello(client, files) is { } hello && hello.App == app && hello.Root == root)
        {
            return client;
        }

        client.Dispose();
        return null;
    }

    /// <summary>Asks the server to bring its window forward: raised, opened, or no-browser.</summary>
    public static string Raise(HttpClient client, ServeFiles files)
    {
        using var request = Keyed(HttpMethod.Post, "/api/raise", files);
        using var response = client.Send(request);
        var raised = response.IsSuccessStatusCode ? response.Content.ReadFromJsonAsync(WindowJson.Default.WindowRaised).GetAwaiter().GetResult() : null;
        return raised?.Window ?? "no-browser";
    }

    /// <summary>A request that carries the key the server wrote.</summary>
    public static HttpRequestMessage Keyed(HttpMethod method, string path, ServeFiles files)
    {
        var request = new HttpRequestMessage(method, path);
        if (files.ReadKey() is { } key)
        {
            request.Headers.Add(ServeFiles.KeyHeader, key);
        }

        return request;
    }

    /// <summary>Waits until the new server answers, which it does once it opened its window, and returns a client of it.</summary>
    public static HttpClient Await(ServeFiles files, string app, string root, string log)
    {
        var deadline = DateTime.UtcNow + Startup;
        while (true)
        {
            var client = Running(files, app, root);
            if (client is not null)
            {
                return client;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new BrainException($"the server of {app} did not answer within {Startup.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds; see {log}");
            }

            Thread.Sleep(Poll);
        }
    }

    /// <summary>Opens <paramref name="url"/> in an app window of class <paramref name="windowClass"/>; says so on stderr when no browser would start.</summary>
    public static bool OpenWindow(BrainEnv env, string? browser, string url, string windowClass, string command)
    {
        if (browser is not null && Browser.Open(browser, url, env.Home, windowClass))
        {
            return true;
        }

        env.Err.WriteLine($"{command}: no Chromium-family browser would start; set {Browser.Variable} to one");
        return false;
    }

    /// <summary>The root the server on the client's port serves, and which window it is, when it answers to the key; else null.</summary>
    private static WindowHello? Hello(HttpClient client, ServeFiles files)
    {
        try
        {
            using var request = Keyed(HttpMethod.Get, "/api/hello", files);
            using var response = client.Send(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            return response.Content.ReadFromJsonAsync(WindowJson.Default.WindowHello).GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
