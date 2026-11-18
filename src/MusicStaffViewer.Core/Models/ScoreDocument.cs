namespace Com.Example.MusicStaffViewer.Core.Models;

public sealed record ScoreDocument(string Path, string Title, string Composer, string? Movement = null)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public bool IsCurrent { get; set; }
    public override string ToString() => Title;
}
