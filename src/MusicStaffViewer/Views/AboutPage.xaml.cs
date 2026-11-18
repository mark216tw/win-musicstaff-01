using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.Example.MusicStaffViewer.Views;

public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        var strings = new ResourceLoader();
        VersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            strings.GetString("VersionFormat"), Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
        FileVersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            strings.GetString("FileVersionFormat"),
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
    }
}
