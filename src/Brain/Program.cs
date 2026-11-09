using Brain;
using Brain.Children;
using Brain.Search;
using Brain.Usage;

// Everything runs inside the one boundary, the world it captures too: a working directory that is gone fails there.
return Cli.Guard(args, ProcessEnv.KnownHome(), ProcessEnv.Session(), Console.Error, () =>
{
    // The agent keeper every claude runs under is no command of the user's: it runs before the data repo is read and is never logged.
    if (args is [AgentKeeper.Name, .. var kept])
    {
        return AgentKeeper.Run(kept);
    }

    // The embedding model is the one the data repo's settings name, or none when they cannot name one.
    var embedder = EmbedderProfiles.Running(ProcessEnv.Home(), ProcessEnv.ModelDirectory());
    using var owned = embedder as IDisposable;
    return Metering.Run(ProcessEnv.Capture(embedder), args);
});
