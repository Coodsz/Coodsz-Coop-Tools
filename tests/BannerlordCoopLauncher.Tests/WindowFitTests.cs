using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

public class WindowFitTests
{
    [Fact]
    public void A_window_bigger_than_the_screen_is_fitted_into_it()
    {
        // 1366 x 768 at 125% scaling leaves about 1093 x 582 logical px above the taskbar.
        var fit = WindowFit.Fit(new WindowSize(1280, 980, 1100, 720), 1093, 582);

        Assert.Equal(1093, fit.Width);
        Assert.Equal(582, fit.Height);
        Assert.Equal(1093, fit.MinWidth);
        Assert.Equal(582, fit.MinHeight);
    }

    [Fact]
    public void A_window_that_fits_is_left_as_it_is()
    {
        var wanted = new WindowSize(1400, 980, 720, 480);
        Assert.Equal(wanted, WindowFit.Fit(wanted, 2560, 1400));
    }

    [Fact]
    public void Only_the_side_that_is_too_big_changes()
    {
        var fit = WindowFit.Fit(new WindowSize(1400, 980, 720, 480), 1536, 824);

        Assert.Equal(1400, fit.Width);
        Assert.Equal(824, fit.Height);
        Assert.Equal(720, fit.MinWidth);
        Assert.Equal(480, fit.MinHeight);
    }

    [Fact]
    public void No_work_area_keeps_the_window_size()
    {
        var wanted = new WindowSize(1280, 980, 1100, 720);
        Assert.Equal(wanted, WindowFit.Fit(wanted, 0, 0));
        Assert.Equal(wanted, WindowFit.Fit(wanted, double.NaN, 900));
    }
    [Fact]
    public void The_report_window_that_fell_below_the_screen_is_moved_up_inside_it()
    {
        // 2560 x 1440 with a 40 px taskbar; the Send my logs window opened at 900,616 and 1001 px tall.
        var work = new ScreenRect(0, 0, 2560, 1400);
        var (left, top) = WindowFit.Clamp(900, 616, 760, 1001, work);

        Assert.Equal(900, left);
        Assert.Equal(399, top);
        Assert.True(top + 1001 <= work.Bottom);
    }

    [Fact]
    public void A_popup_is_centered_on_its_owner_and_kept_inside_the_work_area()
    {
        var work = new ScreenRect(0, 0, 2560, 1400);

        Assert.Equal((900d, 200d), WindowFit.Place(760, 1000, new ScreenRect(780, 500, 1000, 400), work));
        // Owner near the bottom right corner: the popup stays fully on screen.
        Assert.Equal((1800d, 400d), WindowFit.Place(760, 1000, new ScreenRect(2200, 1100, 800, 600), work));
        // No owner: centered on the work area.
        Assert.Equal((900d, 200d), WindowFit.Place(760, 1000, null, work));
    }

    [Fact]
    public void A_second_screen_to_the_left_is_used_as_it_is()
    {
        var work = new ScreenRect(-1920, 0, 1920, 1040);
        var (left, top) = WindowFit.Clamp(-200, 500, 760, 900, work);

        Assert.Equal(-760, left);
        Assert.Equal(140, top);
    }

    [Fact]
    public void A_window_bigger_than_the_work_area_keeps_its_top_left_corner_on_screen()
    {
        var (left, top) = WindowFit.Clamp(300, 300, 1200, 900, new ScreenRect(0, 0, 1093, 582));
        Assert.Equal((0d, 0d), (left, top));
        Assert.Equal((300d, 300d), WindowFit.Clamp(300, 300, 1200, 900, new ScreenRect(0, 0, 0, 0)));
    }
}
