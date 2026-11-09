using System.Globalization;
using Brain.Installing;
using Brain.Search;
using Brain.Serving;

namespace Brain.Commands;

/// <summary>
/// <c>brain serve</c>: runs brain's window server on 127.0.0.1 and opens its window. It keeps running when the terminal
/// that started it closes, and stops on an interrupt or a terminate signal. <c>brain ui</c> starts it in the background.
/// On <c>brain ui --restart</c> it starts its successor itself, from its own environment, on the build now published.
/// </summary>
internal static class ServeCommand
{
    public const string Name = "serve";
    public const string PortVariable = "BRAIN_PORT";
    public const int DefaultPort = 47474;
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Usage);
        var browser = Browser.Find();
        var theme = new OmarchyTheme(env.UserHome, OmarchyTheme.FindCommand());
        var root = ProcessEnv.ModelDirectory();
        using var models = new ModelSwitch(env, root, Installation.Mirror(), EmbedderProfiles.All, profile => new OnnxEmbedder(root, profile));
        using var gpu = GpuWatch.Start(TimeProvider.System);
        var server = new ReviewServer(env, Port(), url => Open(env, browser, url), TimeProvider.System, theme, () => ServeProcess.Start(env), models, gpu);
        Foreground.Run(stop => server.Serve(window: true, stop));
        return ExitCodes.Ok;
    }

    /// <summary>The port brain's window server listens on: BRAIN_PORT, or 47474.</summary>
    public static int Port()
    {
        var value = Environment.GetEnvironmentVariable(PortVariable);
        if (string.IsNullOrEmpty(value))
        {
            return DefaultPort;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and < 65536
            ? port
            : throw new BrainException($"{PortVariable} is {value}, not a port");
    }

    private static bool Open(BrainEnv env, string? browser, string url)
    {
        if (browser is not null && Browser.Open(browser, url, env.Home))
        {
            return true;
        }

        env.Err.WriteLine($"brain serve: no Chromium-family browser would start; set {Browser.Variable} to one");
        return false;
    }
}
