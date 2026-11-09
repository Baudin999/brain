using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Brain.Usage;

namespace Brain.Serving;

/// <summary>
/// The HTTP both of brain's servers speak, brain serve and a workspace's: the boundary each request is answered in, the
/// guard against another host or origin, the key, and reading and sending JSON, text and events. What each server
/// answers, and who may use it, is its own.
/// </summary>
// navarone: 0000182
internal static class WebCalls
{
    public const string JsonType = "application/json; charset=utf-8";

    /// <summary>The largest body a call reads unless it says otherwise.</summary>
    public const int MaxBody = 64 * 1024;

    /// <summary>
    /// Answers one request through <paramref name="route"/>, with the page's policy and headers. A failure answers 500 with
    /// what failed, which the window shows where the user acted, goes to stderr and to the usage log of
    /// <paramref name="home"/> as an error of <paramref name="server"/>, and never takes the server down. A window that went
    /// away while it was answered is no failure: nobody is left to tell. The page's policy is <see cref="WebAssets.Policy"/>
    /// unless <paramref name="policy"/> says another.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A server's one boundary: every failure is answered, logged and recorded, never dropped with the task.")]
    public static void Answer(HttpListenerContext context, string home, string server, TextWriter err, Action<HttpListenerRequest, HttpListenerResponse> route, string policy = WebAssets.Policy)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            response.Headers["Content-Security-Policy"] = policy;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["Cache-Control"] = "no-store";
            route(request, response);
        }
        catch (Exception gone) when (gone is HttpListenerException or ObjectDisposedException)
        {
            TrySend(response, HttpStatusCode.InternalServerError, gone.Message);
        }
        catch (Exception error)
        {
            var told = Failures.Record(home, $"{server} {request.HttpMethod} {request.Url?.AbsolutePath}", error, session: null, err);
            err.WriteLine(server + ": " + told);
            TrySend(response, HttpStatusCode.InternalServerError, told);
        }
        finally
        {
            response.Close();
        }
    }

    /// <summary>
    /// Refuses a request that names any host but the server's own, which shuts out DNS rebinding, and a POST a browser
    /// sends from another origin; true when it refused.
    /// </summary>
    public static bool RefusedStranger(HttpListenerRequest request, HttpListenerResponse response, string origin)
    {
        var host = origin["http://".Length..];
        if (!string.Equals(request.Headers["Host"], host, StringComparison.Ordinal))
        {
            Refuse(response, HttpStatusCode.Forbidden, "this server answers only " + host);
            return true;
        }

        var from = request.Headers["Origin"];
        if (request.HttpMethod == "POST" && from is not null && !string.Equals(from, origin, StringComparison.Ordinal))
        {
            Refuse(response, HttpStatusCode.Forbidden, "a request from another origin");
            return true;
        }

        return false;
    }

    /// <summary>True when the request carries <paramref name="key"/> in its key header.</summary>
    public static bool HasKey(HttpListenerRequest request, string key) =>
        key.Length > 0 && request.Headers[ServeFiles.KeyHeader] is { } given
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(key));

    /// <summary>
    /// The request's body as <typeparamref name="T"/>, sent with its length or chunked; default when it is longer than
    /// <paramref name="max"/> or no JSON of it.
    /// </summary>
    public static T? Read<T>(HttpListenerRequest request, JsonTypeInfo<T> type, int max = MaxBody)
    {
        if (request.ContentLength64 > max)
        {
            return default;
        }

        var body = Body(request.InputStream, max);
        if (body is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(body, type);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>The bytes of <paramref name="input"/> to its end, or null once it holds more than <paramref name="max"/>.</summary>
    private static byte[]? Body(Stream input, int max)
    {
        using var body = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = input.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                return body.ToArray();
            }

            body.Write(chunk, 0, read);
            if (body.Length > max)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Sends what <paramref name="view"/> reads, or 404 with the refusal when it names nothing there is. A failure is no
    /// refusal: it goes on to <see cref="Answer"/>, which answers 500 and records it.
    /// </summary>
    public static void Reading<T>(HttpListenerResponse response, Func<T> view, JsonTypeInfo<T> type)
    {
        try
        {
            Send(response, view(), type);
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            Refuse(response, HttpStatusCode.NotFound, refusal.Message);
        }
    }

    /// <summary>Says <paramref name="text"/> on an event stream, at once.</summary>
    public static void Say(HttpListenerResponse response, string text)
    {
        response.OutputStream.Write(Encoding.UTF8.GetBytes(text));
        response.OutputStream.Flush();
    }

    public static void SendText(HttpListenerResponse response, string text, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = type;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
    }

    public static void Send<T>(HttpListenerResponse response, T value, JsonTypeInfo<T> type)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
        response.ContentType = JsonType;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
    }

    public static void Refuse(HttpListenerResponse response, HttpStatusCode status, string error)
    {
        response.StatusCode = (int)status;
        Send(response, new Problem(error), ReviewJson.Default.Problem);
    }

    private static void TrySend(HttpListenerResponse response, HttpStatusCode status, string error)
    {
        try
        {
            Refuse(response, status, error);
        }
        catch (Exception failure) when (failure is IOException or HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The client is gone or the headers went out; the finally closes the response either way.
        }
    }
}
