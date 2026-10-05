namespace CazadorYTM.Tests;

using Xunit;
using CazadorYTM.Core.Services;

public class AppUpdaterTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2.1", "1.2.0", true)]
    [InlineData("1.1.0", "1.2.0", false)]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.1.1", "1.1.0", true)]
    [InlineData("v1.2.0", "v1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.1.0", "1.1", false)]
    [InlineData("1.1", "1.1.0", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("1.1", "1.1", false)]
    [InlineData("1.2", "1.1", true)]
    public void IsRemoteVersionNewer_EvaluatesCorrectly(string remote, string local, bool expected)
    {
        var result = AppUpdaterService.IsRemoteVersionNewer(remote, local);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatReleaseNotesForDisplay_CleansMarkdown()
    {
        var markdown = "## Título\n\n- **Elemento**: `código`";
        var cleaned = AppUpdaterService.FormatReleaseNotesForDisplay(markdown);
        Assert.DoesNotContain("##", cleaned);
        Assert.DoesNotContain("**", cleaned);
        Assert.DoesNotContain("`", cleaned);
        Assert.Contains("• Elemento: código", cleaned);
    }

    [Fact]
    public void GenerateUpdateBatchScript_ContainsTimeoutAndFallbackKill()
    {
        var src = "C:\\Temp\\Source";
        var dst = "C:\\App";
        var script = AppUpdaterService.GenerateUpdateBatchScript(src, dst);

        Assert.Contains("set RETRIES=0", script);
        Assert.Contains("if %RETRIES% geq 30", script);
        Assert.Contains("taskkill /F /IM CazadorYTM.Gui.exe", script);
        Assert.Contains("xcopy \"C:\\Temp\\Source\\*\" \"C:\\App\\\"", script);
    }

    [Fact]
    public void GenerateUpdateBatchScript_WithTempRoot_ContainsSelfCleanup()
    {
        var src = "C:\\Temp\\Source";
        var dst = "C:\\App";
        var tempRoot = "C:\\Temp\\CazadorYTM_Update";
        var script = AppUpdaterService.GenerateUpdateBatchScript(src, dst, tempRoot);

        Assert.Contains("rd /s /q \\\"C:\\Temp\\CazadorYTM_Update\\\"", script);
    }

    [Fact]
    public void CleanStaleUpdateArtifacts_DeletesTargetDirectory()
    {
        var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CazadorTest_UpdateArtifact_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempFolder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempFolder, "dummy.zip"), "test");

        var result = AppUpdaterService.CleanStaleUpdateArtifacts(tempFolder);

        Assert.True(result);
        Assert.False(System.IO.Directory.Exists(tempFolder));
    }
}
