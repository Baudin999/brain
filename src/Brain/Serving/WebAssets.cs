using System.Net;
using System.Reflection;

namespace Brain.Serving;

/// <summary>
/// What brain's window and a workspace window serve alike: the page's policy and the shared files under
/// <c>Serving/shared</c>. The stylebook, its tokens and its components, is one for both, and so are lit-html, mermaid,
/// markdown and its viewer, the diagrams and the probe. Each server adds the files of its own page to these.
/// </summary>
internal static class WebAssets
{
    public const string Script = "text/javascript; charset=utf-8";
    public const string Sheet = "text/css; charset=utf-8";
    public const string Page = "text/html; charset=utf-8";

    /// <summary>The page's policy: everything from this origin only, no inline script or style, and no frame around it.</summary>
    public const string Policy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; font-src 'self'; "
        + "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly (string Path, string Resource, string Type)[] Shared =
    [
        ("/tokens.css", "shared/tokens.css", Sheet),
        ("/components.css", "shared/components.css", Sheet),
        ("/base.css", "shared/base.css", Sheet),
        ("/prose.css", "shared/prose.css", Sheet),
        ("/lit.js", "shared/lit.js", Script),
        ("/offer.js", "shared/offer.js", Script),
        ("/components/markdown.js", "shared/components/markdown.js", Script),
        ("/components/image-viewer.js", "shared/components/image-viewer.js", Script),
        ("/components/image-viewer.css", "shared/components/image-viewer.css", Sheet),
        ("/components/permission-request.js", "shared/components/permission-request.js", Script),
        ("/components/permission-request.css", "shared/components/permission-request.css", Sheet),
        ("/components/question.js", "shared/components/question.js", Script),
        ("/components/question.css", "shared/components/question.css", Sheet),
        ("/components/stop-confirm.js", "shared/components/stop-confirm.js", Script),
        ("/components/stop-confirm.css", "shared/components/stop-confirm.css", Sheet),
        ("/components/image-thumb.js", "shared/components/image-thumb.js", Script),
        ("/components/image-thumb.css", "shared/components/image-thumb.css", Sheet),
        ("/components/file-explorer.js", "shared/components/file-explorer.js", Script),
        ("/components/file-explorer.css", "shared/components/file-explorer.css", Sheet),
        ("/components/process-fold.js", "shared/components/process-fold.js", Script),
        ("/components/process-fold.css", "shared/components/process-fold.css", Sheet),
        ("/components/agent-row.js", "shared/components/agent-row.js", Script),
        ("/components/agent-message.js", "shared/components/agent-message.js", Script),
        ("/components/trail-step.js", "shared/components/trail-step.js", Script),
        ("/components/navarone-signal.js", "shared/components/navarone-signal.js", Script),
        ("/components/toggle.js", "shared/components/toggle.js", Script),
        ("/components/toggle.css", "shared/components/toggle.css", Sheet),
        ("/components/field-row.js", "shared/components/field-row.js", Script),
        ("/components/field-row.css", "shared/components/field-row.css", Sheet),
        ("/components/verdict.js", "shared/components/verdict.js", Script),
        ("/components/tool-call.js", "shared/components/tool-call.js", Script),
        ("/components/tool-run.js", "shared/components/tool-run.js", Script),
        ("/components/info-panel.js", "shared/components/info-panel.js", Script),
        ("/components/tool-detail.js", "shared/components/tool-detail.js", Script),
        ("/components/tool-call.css", "shared/components/tool-call.css", Sheet),
        ("/components/tool-run.css", "shared/components/tool-run.css", Sheet),
        ("/components/info-panel.css", "shared/components/info-panel.css", Sheet),
        ("/components/tool-detail.css", "shared/components/tool-detail.css", Sheet),
        ("/tokens.js", "shared/tokens.js", Script),
        ("/markdown.js", "shared/markdown.js", Script),
        ("/diagrams.js", "shared/diagrams.js", Script),
        ("/probe.js", "shared/probe.js", Script),
        ("/window.js", "shared/window.js", Script),
        ("/lit-html.js", "shared/vendor/lit-html.js", Script),
        ("/mermaid.js", "shared/vendor/mermaid.min.js", Script),
    ];

    /// <summary>The shared files and <paramref name="own"/>, by the path each is served at.</summary>
    public static Dictionary<string, (string Resource, string Type)> With(params (string Path, string Resource, string Type)[] own)
    {
        var assets = new Dictionary<string, (string Resource, string Type)>(StringComparer.Ordinal);
        foreach (var (path, resource, type) in Shared.Concat(own))
        {
            assets.Add(path, (resource, type));
        }

        return assets;
    }

    /// <summary>Sends one file built into brain.</summary>
    public static void Send(HttpListenerResponse response, string resource, string type)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} is not built into brain");
        response.ContentType = type;
        stream.CopyTo(response.OutputStream);
    }
}
