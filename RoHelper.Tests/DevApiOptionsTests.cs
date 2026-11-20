using RoHelper.DevApi;

namespace RoHelper.Tests;

public class DevApiOptionsTests
{
    [Fact]
    public void NoFlag_Null() => Assert.Null(DevApiOptions.Parse(["--other"]));

    [Fact]
    public void Empty_Null() => Assert.Null(DevApiOptions.Parse([]));

    [Theory]
    [InlineData("--dev-api", 8765)]
    [InlineData("--dev-api=9000", 9000)]
    [InlineData("--dev-api=80", 8765)]
    [InlineData("--dev-api=70000", 8765)]
    [InlineData("--dev-api=abc", 8765)]
    [InlineData("--dev-api=", 8765)]
    public void Port(string arg, int port) => Assert.Equal(port, DevApiOptions.Parse(["x", arg])!.Port);
}
