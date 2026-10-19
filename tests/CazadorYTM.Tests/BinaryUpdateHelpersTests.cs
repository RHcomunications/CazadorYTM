namespace CazadorYTM.Tests;

using System;
using System.IO;
using CazadorYTM.Core.Services;

public class BinaryUpdateHelpersTests : IDisposable
{
    private readonly string _dir;

    public BinaryUpdateHelpersTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"binupd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("1.2.9", "1.2.10", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2.0", "1.99.99", 1)]
    [InlineData("2024-01-02", "2024.1.1", 1)]
    public void CompareVersionStrings_ComparesNumericParts(string remote, string local, int expectedSign)
    {
        var result = BinaryUpdateHelpers.CompareVersionStrings(remote, local);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Fact]
    public void SafeReplaceFile_MovesWhenTargetMissing()
    {
        var source = Path.Combine(_dir, "source.bin");
        var target = Path.Combine(_dir, "target.bin");
        File.WriteAllText(source, "nuevo");

        BinaryUpdateHelpers.SafeReplaceFile(source, target);

        Assert.False(File.Exists(source));
        Assert.Equal("nuevo", File.ReadAllText(target));
    }

    [Fact]
    public void SafeReplaceFile_ReplacesExistingTarget()
    {
        var source = Path.Combine(_dir, "source2.bin");
        var target = Path.Combine(_dir, "target2.bin");
        File.WriteAllText(source, "nuevo");
        File.WriteAllText(target, "viejo");

        BinaryUpdateHelpers.SafeReplaceFile(source, target);

        Assert.False(File.Exists(source));
        Assert.Equal("nuevo", File.ReadAllText(target));
    }

    [Fact]
    public void SafeReplaceFile_CreatesTargetDirectory()
    {
        var source = Path.Combine(_dir, "source3.bin");
        var target = Path.Combine(_dir, "sub", "deep", "target3.bin");
        File.WriteAllText(source, "contenido");

        BinaryUpdateHelpers.SafeReplaceFile(source, target);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Equal("contenido", File.ReadAllText(target));
    }
}
