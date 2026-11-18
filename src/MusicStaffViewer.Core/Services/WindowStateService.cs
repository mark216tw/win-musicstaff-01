using Com.Example.MusicStaffViewer.Core.Models;

namespace Com.Example.MusicStaffViewer.Core.Services;

public static class WindowStateService
{
    public static WindowBounds Clamp(WindowBounds saved, WorkArea area)
    {
        int width = Math.Min(Math.Max(saved.Width, 900), area.Width);
        int height = Math.Min(Math.Max(saved.Height, 600), area.Height);
        int x = Math.Clamp(saved.X, area.X, area.X + area.Width - width);
        int y = Math.Clamp(saved.Y, area.Y, area.Y + area.Height - height);
        return new WindowBounds(x, y, width, height, saved.IsMaximized);
    }
}
