namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

public static class ProcessTracker
{
    private static readonly ConcurrentDictionary<int, Process> TrackedProcesses = new();

    public static void Track(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            TrackedProcesses[process.Id] = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => TrackedProcesses.TryRemove(process.Id, out _);
        }
        catch
        {
        }
    }

    public static void KillAll(TimeSpan? waitTimeout = null)
    {
        var timeout = waitTimeout ?? TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow + timeout;
        foreach (var process in TrackedProcesses.Values.ToArray())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }

        foreach (var process in TrackedProcesses.Values.ToArray())
        {
            try
            {
                var remaining = deadline - DateTime.UtcNow;
                process.WaitForExit(remaining > TimeSpan.Zero ? (int)remaining.TotalMilliseconds : 0);
            }
            catch
            {
            }

            TrackedProcesses.TryRemove(process.Id, out _);
        }
    }
}
