using CommunityToolkit.Mvvm.ComponentModel;

namespace Com.Example.MusicStaffViewer.ViewModels;

public sealed partial class ScoreTrackViewModel(int index, string displayName, bool isPercussion)
    : ObservableObject
{
    private bool _isSelected;

    public int Index => index;
    public string DisplayName => displayName;
    public bool IsPercussion => isPercussion;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
