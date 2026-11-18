using Com.Example.MusicStaffViewer.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Com.Example.MusicStaffViewer.ViewModels;

public sealed class ScorePartialViewModel(ScorePartialLayout layout, int index, double topGap = 0)
    : ObservableObject
{
    private ImageSource? _imageSource;
    private bool _isLoading;
    private double _displayWidth = layout.Width;
    private double _displayHeight = layout.Height;
    private double _leftInset = layout.X;
    private Thickness _displayMargin = new(layout.X, topGap, 0, 0);

    public int Index => index;
    public string Id => layout.Id;
    public double BaseX => layout.X;
    public double BaseY => layout.Y;
    public double BaseWidth => layout.Width;
    public double BaseHeight => layout.Height;
    public double DisplayWidth { get => _displayWidth; private set => SetProperty(ref _displayWidth, value); }
    public double DisplayHeight { get => _displayHeight; private set => SetProperty(ref _displayHeight, value); }
    public double LeftInset { get => _leftInset; private set => SetProperty(ref _leftInset, value); }
    public Thickness DisplayMargin { get => _displayMargin; private set => SetProperty(ref _displayMargin, value); }
    public ImageSource? ImageSource { get => _imageSource; set => SetProperty(ref _imageSource, value); }
    public bool IsVisible { get; set; }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool IsDecoding { get; set; }
    public byte[]? PngBytes { get; set; }
    public double SourceRasterScale { get; set; } = AlphaTabScoreSession.MinimumRasterScale;
    public double DecodedZoom { get; set; } = double.NaN;
    public int FirstMasterBarIndex { get; private set; } = -1;
    public int LastMasterBarIndex { get; private set; } = -1;
    public bool HasMasterBarRange =>
        FirstMasterBarIndex >= 0 && LastMasterBarIndex >= FirstMasterBarIndex;

    public void SetMasterBarRange(int first, int last)
    {
        if (first < 0 || last < first)
        {
            return;
        }

        FirstMasterBarIndex = first;
        LastMasterBarIndex = last;
    }

    public void SetZoom(double zoom)
    {
        DisplayWidth = layout.Width * zoom;
        DisplayHeight = layout.Height * zoom;
        LeftInset = layout.X * zoom;
        DisplayMargin = new Thickness(LeftInset, topGap * zoom, 0, 0);
    }
}
