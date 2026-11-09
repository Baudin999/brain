using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Brain.Code;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// How a server reads a request's body, over real HTTP on 127.0.0.1: a body sent with its length and one sent chunked,
/// as <c>JsonContent</c> sends it, read alike, and a chunked body past the limit reads as none.
/// </summary>
public sealed class WebCallsTests
{
    [Fact]
    public async Task AChunkedBodyReadsAsOneWithItsLength()
    {
        using var chunked = JsonContent.Create(new CodeSay("s1", "hello"), CodeJson.Default.CodeSay);
        using var sized = new StringContent("""{"session":"s1","words":"hello"}""", Encoding.UTF8, "application/json");

        var fromChunked = await Echo(chunked, WebCalls.MaxBody);
        var fromSized = await Echo(sized, WebCalls.MaxBody);

        Assert.Null(chunked.Headers.ContentLength);
        Assert.Equal(new CodeSay("s1", "hello"), fromChunked);
        Assert.Equal(new CodeSay("s1", "hello"), fromSized);
    }

    [Fact]
    public async Task AChunkedBodyPastTheLimitReadsAsNone()
    {
        using var large = JsonContent.Create(new CodeSay("s1", new string('x', 2048)), CodeJson.Default.CodeSay);

        var read = await Echo(large, 1024);

        Assert.Null(read);
    }

    /// <summary>What a server reads of <paramref name="content"/> with <see cref="WebCalls.Read"/> and a limit of <paramref name="max"/>.</summary>
    private static async Task<CodeSay?> Echo(HttpContent content, int max)
    {
        var origin = new Uri("http://127.0.0.1:" + FreePort() + "/");
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin.AbsoluteUri);
        listener.Start();
        using var client = new HttpClient();
        var sent = client.PostAsync(origin, content);
        var context = await listener.GetContextAsync().ConfigureAwait(false);
        var read = WebCalls.Read(context.Request, CodeJson.Default.CodeSay, max);
        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
        context.Response.Close();
        using var response = await sent.ConfigureAwait(false);
        return read;
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
