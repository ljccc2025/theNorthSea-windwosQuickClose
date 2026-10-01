using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Core.ViewModels;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class CardViewModelFactoryTests
{
    [Fact]
    public void Create_PlainSwitch_ReturnsToggleCard()
    {
        var card = CardViewModelFactory.Create(new FakeSwitch());

        Assert.IsType<SwitchCardViewModel>(card);
    }

    [Fact]
    public void Create_ChoiceSwitch_ReturnsChoiceCard()
    {
        var card = CardViewModelFactory.Create(new FakeChoiceSwitch());

        Assert.IsType<ChoiceCardViewModel>(card);
    }

    [Fact]
    public void Create_RegistrySwitchCards_MapToExpectedCardTypes()
    {
        var registry = SwitchRegistry.CreateDefault(
            new PowerShellRunner(new ProcessRunner()),
            new FakeRegistryStore(),
            new FakeSettingsNotifier(),
            new FakePowerCfg(),
            SessionOwnerState.SameAccount);

        var cards = registry.All.Select(CardViewModelFactory.Create).ToArray();

        Assert.Collection(
            cards,
            card => Assert.IsType<SwitchCardViewModel>(card),
            card => Assert.IsType<SwitchCardViewModel>(card),
            card => Assert.IsType<SwitchCardViewModel>(card),
            card => Assert.IsType<SwitchCardViewModel>(card),
            card => Assert.IsType<ChoiceCardViewModel>(card),
            card => Assert.IsType<SwitchCardViewModel>(card));

        // 分组名字直接透传给分组视图。
        Assert.Equal(
            [SwitchGroup.Security, SwitchGroup.Network, SwitchGroup.Power, SwitchGroup.Power, SwitchGroup.Power, SwitchGroup.System],
            cards.Select(card => card.Group));
    }
}
