using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class FieldGateTests
{
    private static readonly Rectangle Btn = new(400, 300, 100, 40);

    private static ScreenState Screen() => new(new Size(1000, 600), 1.0);

    [Fact]
    public void StartsOutside()
    {
        Assert.False(new FieldGate().InField);
    }

    [Fact]
    public void StartsRun_Enters_WithTime()
    {
        var gate = new FieldGate();

        gate.Update(Screen() with { GoField = Btn }, new FlowDecision("準備前往野外", new Point(1, 1), StartsRun: true), 1234);

        Assert.True(gate.InField);
        Assert.Equal(1234, gate.Since);
    }

    [Fact]
    public void ExpeditionLobby_TwoFrames_Leaves()
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(Screen() with { ExpeLeader = Btn }, new FlowDecision("走向遠征隊長"), 1000);
        Assert.True(gate.InField);
        gate.Update(Screen() with { StageList = Btn }, new FlowDecision("② 選擇地圖：斐揚森林"), 1250);
        Assert.False(gate.InField);
    }

    [Fact]
    public void ExpeditionDialogs_AreLobbyVisible()
    {
        Assert.True(FieldGate.LobbyVisible(Screen() with { DiffTitle = Btn }));
        Assert.True(FieldGate.LobbyVisible(Screen() with { LeaderPrompt = Btn }));
    }

    [Fact]
    public void FieldScreen_StaysInside()
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(Screen(), new FlowDecision("待機"), 5000);

        Assert.True(gate.InField);
        Assert.Equal(0, gate.Since);
    }

    [Fact]
    public void CountRun_Leaves()
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(Screen(), new FlowDecision("結算中", CountRun: true), 1);

        Assert.False(gate.InField);
    }

    [Theory]
    [InlineData(ScreenTemplates.Solo)]
    [InlineData(ScreenTemplates.Start)]
    [InlineData(ScreenTemplates.FestivalHost)]
    [InlineData(ScreenTemplates.Title)]
    [InlineData(ScreenTemplates.CharSelect)]
    public void LobbyTemplate_TwoTicks_Leaves(string name)
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(With(name), new FlowDecision("待機"), 1);
        Assert.True(gate.InField);
        gate.Update(With(name), new FlowDecision("待機"), 2);

        Assert.False(gate.InField);
    }

    /// <summary>實測野外畫面偶爾有一格讓範本誤判：只看到一格不能離開野外</summary>
    [Fact]
    public void LobbyTemplate_OneFrame_StaysInside()
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(With(ScreenTemplates.Start), new FlowDecision("待機"), 1);
        gate.Update(Screen(), new FlowDecision("待機"), 2);
        gate.Update(With(ScreenTemplates.Start), new FlowDecision("待機"), 3);

        Assert.True(gate.InField);
    }

    [Theory]
    [InlineData(ScreenTemplates.GoField)]
    [InlineData(ScreenTemplates.TalkPrompt)]
    [InlineData(ScreenTemplates.Return)]
    public void OtherTemplates_DoNotLeave(string name)
    {
        var gate = new FieldGate();
        gate.Enter(0);

        gate.Update(With(name), new FlowDecision("待機"), 1);

        Assert.True(gate.InField);
    }

    [Fact]
    public void LobbyVisible_AnyTemplate()
    {
        Assert.False(FieldGate.LobbyVisible(Screen()));
        foreach (var name in ScreenTemplates.All.Append(ScreenTemplates.Discard).Append(ScreenTemplates.DiscardOk)
                     .Append(ScreenTemplates.Title).Append(ScreenTemplates.CharSelect))
            Assert.True(FieldGate.LobbyVisible(With(name)), name);
    }

    private static ScreenState With(string name) => name switch
    {
        ScreenTemplates.Solo => Screen() with { Solo = Btn },
        ScreenTemplates.Start => Screen() with { Start = Btn },
        ScreenTemplates.GoField => Screen() with { GoField = Btn },
        ScreenTemplates.Return => Screen() with { Return = Btn },
        ScreenTemplates.FestivalHost => Screen() with { FestivalHost = Btn },
        ScreenTemplates.TalkPrompt => Screen() with { TalkPrompt = Btn },
        ScreenTemplates.Discard => Screen() with { Discard = Btn },
        ScreenTemplates.DiscardOk => Screen() with { DiscardOk = Btn },
        ScreenTemplates.Title => Screen() with { Title = Btn },
        ScreenTemplates.CharSelect => Screen() with { CharSelect = Btn },
        _ => throw new ArgumentException(name),
    };
}
