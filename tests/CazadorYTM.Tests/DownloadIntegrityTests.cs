namespace CazadorYTM.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using CazadorYTM.Core.Services;

public class DownloadIntegrityTests
{
    [Theory]
    [InlineData("sha256:ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  file.zip", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
    public void TryNormalizeSha256_ExtractsDigest(string value, string expected)
    {
        Assert.True(DownloadIntegrity.TryNormalizeSha256(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256:no-es-un-hash")]
    [InlineData("abc")]
    [InlineData(null)]
    public void TryNormalizeSha256_Invalid_ReturnsFalse(string? value)
        => Assert.False(DownloadIntegrity.TryNormalizeSha256(value, out _));

    [Fact]
    public void ParseChecksumFile_ReadsHash()
        => Assert.Equal(
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            DownloadIntegrity.ParseChecksumFile("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  archive.zip"));

    [Fact]
    public void ParseChecksumFile_Invalid_ReturnsNull()
        => Assert.Null(DownloadIntegrity.ParseChecksumFile("not a checksum"));

    [Fact]
    public async Task VerifyFileAsync_MatchingHash_Succeeds()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "hello");
            await DownloadIntegrity.VerifyFileAsync(
                path,
                "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_MismatchedHash_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "hello");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                DownloadIntegrity.VerifyFileAsync(
                    path,
                    "0000000000000000000000000000000000000000000000000000000000000000"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
