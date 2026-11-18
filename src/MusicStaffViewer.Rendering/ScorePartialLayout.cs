namespace Com.Example.MusicStaffViewer.Rendering;

public sealed record ScorePartialLayout(string Id, double X, double Y, double Width, double Height,
    int FirstMasterBarIndex = -1, int LastMasterBarIndex = -1);
