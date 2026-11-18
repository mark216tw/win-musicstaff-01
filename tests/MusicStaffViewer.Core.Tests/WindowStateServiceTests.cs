using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.Services;
using Xunit;

namespace Com.Example.MusicStaffViewer.Core.Tests;

public sealed class WindowStateServiceTests
{
    [Fact]
    public void MovesOffScreenWindowBackIntoWorkArea()
    {
        WindowBounds result = WindowStateService.Clamp(new WindowBounds(-4000, 4000, 1200, 800, true),
            new WorkArea(0, 0, 1920, 1040));
        Assert.Equal(new WindowBounds(0, 240, 1200, 800, true), result);
    }

    [Fact]
    public void FitsWindowWhenWorkAreaIsSmallerThanMinimum()
    {
        WindowBounds result = WindowStateService.Clamp(new WindowBounds(0, 0, 1200, 800),
            new WorkArea(0, 0, 700, 500));
        Assert.Equal(new WindowBounds(0, 0, 700, 500), result);
    }
}
