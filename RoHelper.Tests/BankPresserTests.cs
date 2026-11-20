using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class BankPresserTests
{
    private static BankStep Step(BankPresser b, long now, bool ready = true, int? mvpLeft = null, bool loot = false, bool auto = true, bool preMvp = true)
        => b.Step(ready, mvpLeft, loot, auto, preMvp, 15, now);

    [Fact]
    public void Ready_PressesOnce_ThenWaitsForPicker()
    {
        var b = new BankPresser();

        Assert.Equal(new BankStep(true, "打開升級", ""), Step(b, 10_000));
        Assert.Equal(new BankStep(false, "打開升級", ""), Step(b, 10_500));
        Assert.Equal(new BankStep(false, "打開升級", ""), Step(b, 11_500));
    }

    [Fact]
    public void PickerSeen_NextPressAllowed()
    {
        var b = new BankPresser();
        Step(b, 10_000);
        b.PickerSeen();

        Assert.True(Step(b, 15_000).Press);
    }

    [Fact]
    public void NoPicker_MutesTenSeconds_WithNote()
    {
        var b = new BankPresser();
        Step(b, 10_000);

        Assert.Equal(new BankStep(false, "", BankPresser.MuteNote), Step(b, 11_600));
        Assert.Equal(BankPresser.MuteNote, Step(b, 21_000).Note);
        Assert.True(Step(b, 21_700).Press);
    }

    [Fact]
    public void NotReady_Nothing()
    {
        Assert.Equal(new BankStep(false, "", ""), Step(new BankPresser(), 10_000, ready: false));
    }

    [Fact]
    public void NotReady_ClearsTry()
    {
        var b = new BankPresser();
        Step(b, 10_000);
        Step(b, 10_500, ready: false);

        Assert.True(Step(b, 12_000).Press);
    }

    [Fact]
    public void LootPhase_NoAutoUpgrade()
    {
        Assert.Equal("", Step(new BankPresser(), 10_000, loot: true).Status);
    }

    [Fact]
    public void AutoUpgradeOff_NoPress()
    {
        Assert.False(Step(new BankPresser(), 10_000, auto: false).Press);
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(1, true)]
    [InlineData(16, false)]
    [InlineData(0, false)]
    public void PreMvp_PressesEvenWithoutAutoUpgrade(int left, bool press)
    {
        var s = Step(new BankPresser(), 10_000, mvpLeft: left, auto: false);

        Assert.Equal(press, s.Press);
        Assert.Equal(press ? $"MVP 還有 {left} 秒，先選完升級" : "", s.Status);
    }

    [Fact]
    public void PreMvpOff_UsesAutoUpgradeLabel()
    {
        Assert.Equal("打開升級", Step(new BankPresser(), 10_000, mvpLeft: 10, preMvp: false).Status);
    }

    [Fact]
    public void NewRun_ClearsMute()
    {
        var b = new BankPresser();
        Step(b, 10_000);
        Step(b, 11_600);
        b.NewRun();

        Assert.True(Step(b, 12_000).Press);
    }
}
