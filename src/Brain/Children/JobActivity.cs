namespace Brain.Children;

/// <summary>
/// Whether each job a claude started is running or idle: a job is idle once it and everything below it used no CPU
/// time for <see cref="Quiet"/> or longer. It keeps, for each live job only, its CPU time at the last read that changed
/// it and when that read was, so a job is known by its pid and its start, and one gone is forgotten at the next read.
/// A job seen for the first time, and one whose CPU time cannot be read, as on macOS, is running.
/// </summary>
internal sealed class JobActivity
{
    /// <summary>How long a job's tree uses no CPU time before it shows as idle.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();
    private Dictionary<(int Pid, string Start), Used> seen = [];

    /// <summary>
    /// Remembers each of <paramref name="jobs"/> as read at <paramref name="now"/>, forgets every job not among them,
    /// and says for each, in their order, since when it is idle, or null while it runs.
    /// </summary>
    public IReadOnlyList<DateTimeOffset?> Read(IReadOnlyList<JobUse> jobs, DateTimeOffset now)
    {
        lock (gate)
        {
            var next = new Dictionary<(int Pid, string Start), Used>();
            var idle = new List<DateTimeOffset?>();
            foreach (var job in jobs)
            {
                var used = UseOf(job, now);
                next[(job.Pid, job.Start)] = used;
                idle.Add(IdleSince(used, now));
            }

            seen = next;
            return idle;
        }
    }

    /// <summary>The job's CPU time and since when it is that, under the gate: kept while it did not change, else from now.</summary>
    private Used UseOf(JobUse job, DateTimeOffset now)
    {
        if (job.Cpu is null)
        {
            return new Used(null, now);
        }

        if (seen.TryGetValue((job.Pid, job.Start), out var last) && last.Cpu == job.Cpu)
        {
            return last;
        }

        return new Used(job.Cpu, now);
    }

    private static DateTimeOffset? IdleSince(Used used, DateTimeOffset now)
    {
        if (used.Cpu is null)
        {
            return null;
        }

        if (now - used.Since < Quiet)
        {
            return null;
        }

        return used.Since;
    }

    /// <summary>A job's CPU time, or null where it cannot be read, and since when it was that.</summary>
    private sealed record Used(long? Cpu, DateTimeOffset Since);
}

/// <summary>One job as read: its pid, its start, and the CPU time its tree used, or null where it cannot be read.</summary>
internal sealed record JobUse(int Pid, string Start, long? Cpu);
