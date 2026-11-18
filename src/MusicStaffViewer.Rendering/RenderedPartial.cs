namespace Com.Example.MusicStaffViewer.Rendering;

public sealed record RenderedPartial(byte[] Png, int FirstMasterBarIndex, int LastMasterBarIndex);
