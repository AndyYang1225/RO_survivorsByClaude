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
}
