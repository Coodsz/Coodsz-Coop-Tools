using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class FastForwardPressTests
{
    [Fact]
    public void A_dedicated_server_player_sends_the_fast_forward_button_to_the_server()
    {
        Assert.True(FastForwardPress.SendButtonToServer(coopSession: true, thisProcessIsServer: false, buttonPressed: true));
        Assert.False(FastForwardPress.LeaveTaomTickAlone(coopSession: true, hostAllowsFastForward: true, thisProcessIsServer: false));
    }

    [Fact]
    public void A_turned_off_host_blocks_the_host_and_every_player()
    {
        Assert.True(FastForwardPress.SendButtonToServer(coopSession: true, thisProcessIsServer: false, buttonPressed: true));
        Assert.False(FastForwardPress.LeaveTaomTickAlone(coopSession: true, hostAllowsFastForward: false, thisProcessIsServer: false));
        Assert.False(FastForwardPress.LeaveTaomTickAlone(coopSession: true, hostAllowsFastForward: false, thisProcessIsServer: true));
    }

    [Fact]
    public void The_server_keeps_its_own_fast_forward_when_the_host_allows_it()
    {
        Assert.True(FastForwardPress.LeaveTaomTickAlone(coopSession: true, hostAllowsFastForward: true, thisProcessIsServer: true));
        Assert.False(FastForwardPress.SendButtonToServer(coopSession: true, thisProcessIsServer: true, buttonPressed: true));
    }
}
