namespace CazadorYTM.Tests;

using Xunit;
using CazadorYTM.Core.Services;

public class AppUpdaterTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.1.1", "1.1.0", true)]
    [InlineData("v1.2.0", "v1.1.0", true)]
    [InlineData("1.1.0", "1.1.0", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("1.1", "1.1", false)]
    [InlineData("1.2", "1.1", true)]
    public void IsRemoteVersionNewer_EvaluatesCorrectly(string remote, string local, bool expected)
    {
        var result = AppUpdaterService.IsRemoteVersionNewer(remote, local);
        Assert.Equal(expected, result);
    }
}
