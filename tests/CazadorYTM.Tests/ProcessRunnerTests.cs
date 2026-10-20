namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using CazadorYTM.Core.Services;

public class ProcessRunnerTests
{
    private static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_TruncatesCapturedOutput()
    {
        var result = await ProcessRunner.RunAsync(
            CmdPath,
            new[] { "/c", "echo abcdefghijklmnopqrstuvwxyz" },
            maxCapturedChars: 8);

        Assert.True(result.Success);
        Assert.True(result.OutputTruncated);
        Assert.True(result.StandardOutput.Length <= 8);
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndMarksResult()
    {
        var result = await ProcessRunner.RunAsync(
            CmdPath,
            new[] { "/c", "ping -n 5 127.0.0.1 >nul" },
            timeoutMs: 100);

        Assert.True(result.TimedOut);
        Assert.False(result.Success);
    }
}
