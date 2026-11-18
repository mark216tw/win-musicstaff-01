namespace Com.Example.MusicStaffViewer.Rendering;

public sealed record ScoreLayoutResult(string Title, string Composer, double Width, double Height,
    IReadOnlyList<ScorePartialLayout> Partials, ScorePartialLayout? Header,
    IReadOnlyList<ScoreTrackInfo> Tracks, IReadOnlyList<int> SelectedTrackIndices, int MasterBarCount,
    IReadOnlyList<ScoreSectionInfo> Sections);
