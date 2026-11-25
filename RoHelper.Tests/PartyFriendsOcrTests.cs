using RoHelper.Vision;
namespace RoHelper.Tests;
public class PartyFriendsOcrTests
{
    [Fact]
    public async Task FriendRowsFound()
    {
        var f = CardLayoutTests.Fixture("好友視窗");
        var lines = await PartyUi.ReadBothAsync(OcrReader.TryCreate(out _)!, f, PartyUi.FriendsArea(f, 1.0));
        Assert.Contains(lines, l => PartyUi.NameLike(l.Text, "挪威鮭魚"));
        Assert.Contains(lines, l => PartyUi.NameLike(l.Text, "鮭魚生魚片"));
        Assert.DoesNotContain(lines, l => PartyUi.NameLike(l.Text, "紐西蘭鮭魚"));
    }
}

public class PartyDialogOcrTests
{
    [Fact]
    public async Task Panel_ThreeMembersAllFound()
    {
        var f = CardLayoutTests.Fixture("隊伍面板3人");
        var panel = await PartyUi.ReadPanelAsync(OcrReader.TryCreate(out _)!, f, 1.0);

        Assert.NotNull(panel);
        Assert.Empty(PartyUi.Missing(["挪威鮭魚", "鮭魚生魚片"], panel));
    }

    [Fact]
    public async Task Invite_LeaderNameRecognized()
    {
        var f = CardLayoutTests.Fixture("組隊邀請");
        var who = await PartyUi.ReadInviterAsync(OcrReader.TryCreate(out _)!, f, 1.0, new Rectangle(1083, 368, 61, 32));

        Assert.NotNull(who);
        Assert.True(PartyUi.NameLike(who!, "紐西蘭鮭魚"), who);
        Assert.False(PartyUi.NameLike(who!, "挪威鮭魚"), who);
    }
}

public class PartyMenuOcrTests
{
    /// <summary>實測：已經組隊的人選單裡沒有「邀請組隊」，最後一項是「檢舉」；絕對不能誤認</summary>
    [Theory]
    [InlineData("好友選單可邀請", true)]
    [InlineData("好友選單已組隊", false)]
    public async Task Menu_InviteItemOnlyWhenPresent(string fixture, bool hasInvite)
    {
        var f = CardLayoutTests.Fixture(fixture);
        var lines = await PartyUi.ReadBothAsync(OcrReader.TryCreate(out _)!, f, new Rectangle(803 - 60, 295 - 260, 120, 262));

        Assert.Equal(hasInvite, lines.Any(l => l.Text.Contains('隊') || l.Text.Contains("邀請")));
    }
}

public class PartyMenuCellOcrTests
{
    /// <summary>選單最後一項那一格單獨讀：別頻道的「邀請組隊」讀得到「隊」，已組隊的人那一格（檢舉）讀不到</summary>
    [Theory]
    [InlineData("好友選單別頻道", 329, true)]
    [InlineData("好友選單已組隊", 266, false)]
    public async Task LastCell(string fixture, int y, bool hasInvite)
    {
        var f = CardLayoutTests.Fixture(fixture);
        Assert.Equal(hasInvite, await PartyUi.IsInviteCellAsync(OcrReader.TryCreate(out _)!, f, 1.0, new Point(791, y)));
    }
}

public class PartyPanelLeaderOcrTests
{
    /// <summary>面板最上面的名字是隊長：自己的隊伍（紐西蘭鮭魚）、被轉成隊員當隊長（挪威鮭魚）</summary>
    [Theory]
    [InlineData("隊伍面板3人", "紐西蘭鮭魚", true)]
    [InlineData("隊伍面板別人當隊長", "紐西蘭鮭魚", false)]
    public async Task Leader(string fixture, string ours, bool isOurs)
    {
        var f = CardLayoutTests.Fixture(fixture);
        var lines = await PartyUi.ReadPanelLinesAsync(OcrReader.TryCreate(out _)!, f, 1.0);

        Assert.NotNull(lines);
        var leader = PartyUi.PanelLeader(lines!);
        Assert.NotNull(leader);
        Assert.Equal(isOurs, PartyUi.NameLike(leader!, ours));
        Assert.NotNull(PartyUi.LeaveButton(lines!, 1.0));
    }
}
