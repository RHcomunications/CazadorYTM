namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CazadorYTM.Core;
using CazadorYTM.Core.Services;
using Xunit;

public class LocalizationAndDiagnosticsTests
{
    [Fact]
    public async Task LocalizationService_ConcurrentReadsDuringLanguageSwitch_NeverThrowsOrReturnsNull()
    {
        var loc = LocalizationService.Instance;
        var originalLang = loc.CurrentLanguage;
        using var cts = new CancellationTokenSource();

        try
        {
            var readerTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 400 && !cts.IsCancellationRequested; i++)
                {
                    var val = loc["Save"];
                    Assert.False(string.IsNullOrEmpty(val));
                    var fallback = loc.Get("NonExistentKey_ConcurrencyTest", "fallback");
                    Assert.Equal("fallback", fallback);
                }
            })).ToArray();

            for (int i = 0; i < 12; i++)
            {
                loc.LoadLanguage(i % 2 == 0 ? "en" : "es");
            }

            await Task.WhenAll(readerTasks);
        }
        finally
        {
            cts.Cancel();
            loc.LoadLanguage(originalLang);
        }
    }

    [Fact]
    public void DiagnosticReportService_SanitizeText_RedactsProxyCredentialsTokensAndUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var raw = $"Proxy http://alice:supersecret@proxy.example.com:8080 | SAPISID=abc123def | {{\"SID\":\"json_sid_secret\"}} | Authorization: Basic dXNlcjpwYXNz | Authorization: Bearer hdr_tok_111 | Bearer tok_98765 | Path {userProfile}\\Music\\track.mp3";

        var sanitized = DiagnosticReportService.SanitizeText(raw);

        Assert.DoesNotContain("alice", sanitized);
        Assert.DoesNotContain("supersecret", sanitized);
        Assert.DoesNotContain("abc123def", sanitized);
        Assert.DoesNotContain("json_sid_secret", sanitized);
        Assert.DoesNotContain("dXNlcjpwYXNz", sanitized);
        Assert.DoesNotContain("hdr_tok_111", sanitized);
        Assert.DoesNotContain("tok_98765", sanitized);
        Assert.Contains("http://<redacted>@proxy.example.com:8080", sanitized);
        Assert.Contains("SAPISID=<redacted>", sanitized);
        Assert.Contains("Bearer <redacted>", sanitized);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            Assert.DoesNotContain(userProfile, sanitized, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<USERPROFILE>", sanitized);
        }
    }

    [Fact]
    public void DiagnosticReportService_BuildReport_IncludesVersionEnginesSanitizedConfigAndTailLogs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cazador_diag_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configPath = Path.Combine(tempDir, "config.json");
            var config = new AppConfig(configPath);
            config.Set("proxy", "http://bob:hunter2@127.0.0.1:8888");
            config.Set("language", "en");

            var logs = string.Join(Environment.NewLine, Enumerable.Range(1, 20).Select(i => $"[INFO] Line {i} SID=secret_{i}"));
            var report = DiagnosticReportService.BuildReport(config, tempDir, logs, maxLogLines: 5);

            Assert.Contains(Constants.AppVersion, report);
            Assert.Contains("UI Language:       en", report);
            Assert.Contains("Proxy Configured: Yes (redacted)", report);
            Assert.DoesNotContain("hunter2", report);
            Assert.DoesNotContain("bob", report);
            Assert.DoesNotContain("secret_20", report);
            Assert.Contains("Line 20 SID=<redacted>", report);
            Assert.DoesNotContain("Line 1 ", report);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SharedHttpClient_ExposesConfiguredSingletons()
    {
        Assert.NotNull(SharedHttpClient.GitHubAndDownloads);
        Assert.NotNull(SharedHttpClient.Metadata);
        Assert.NotEmpty(SharedHttpClient.GitHubAndDownloads.DefaultRequestHeaders.UserAgent);
        Assert.NotEmpty(SharedHttpClient.Metadata.DefaultRequestHeaders.UserAgent);

        using var custom = SharedHttpClient.CreateClient(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            "CazadorYTM-Test/1.0");
        Assert.Equal(TimeSpan.FromSeconds(15), custom.Timeout);
    }
}
