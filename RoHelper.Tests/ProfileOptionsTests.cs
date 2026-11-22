namespace RoHelper.Tests;

public class ProfileOptionsTests
{
    [Fact]
    public void NoArg_EmptyName()
    {
        Assert.Equal("", ProfileOptions.Parse(["--dev-api"], out var error));
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("--profile=A", "A")]
    [InlineData("--profile=acct_2", "acct_2")]
    [InlineData("--PROFILE=b-1", "b-1")]
    public void ValidName(string arg, string name)
        => Assert.Equal(name, ProfileOptions.Parse(["--dev-api=8766", arg], out _));

    [Theory]
    [InlineData("--profile")]
    [InlineData("--profile=")]
    [InlineData("--profile=../x")]
    [InlineData("--profile=中文")]
    [InlineData("--profile=a b")]
    [InlineData("--profile=123456789012345678901234567890123")]
    public void InvalidName_NullWithError(string arg)
    {
        Assert.Null(ProfileOptions.Parse([arg], out var error));
        Assert.Contains("--profile", error);
    }

    [Fact]
    public void LastOneWins()
        => Assert.Equal("B", ProfileOptions.Parse(["--profile=A", "--profile=B"], out _));

    /// <summary>新 profile 的視窗大小照來源（遊戲介面隨視窗高度縮放，範本是在那個大小存的），位置錯開</summary>
    [Fact]
    public void SeedAppSettings_CopiesSizeOffsetsPosition()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rohelper-seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "app.json");
            File.WriteAllText(src, "{ \"GameBounds\": { \"X\": 42, \"Y\": 178, \"Width\": 1920, \"Height\": 745 }, \"PanelVisible\": false, \"MuteAudio\": false }");
            var dst = Path.Combine(dir, "B", "app.json");

            RoHelper.Storage.AppSettings.SeedFrom(src, dst, offset: 30);

            var json = File.ReadAllText(dst);
            Assert.Contains("1920", json);
            Assert.Contains("745", json);
            Assert.Contains("72", json);
            Assert.Contains("208", json);
            Assert.Contains("\"MuteAudio\": false", json);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
