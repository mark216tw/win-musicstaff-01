using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.Example.MusicStaffViewer.Views;

public sealed partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        ResourceLoader strings = App.Strings;
        VersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            strings.GetString("VersionFormat"), GetInformationalVersion());
        FileVersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            strings.GetString("FileVersionFormat"),
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);
    }

    private static string? GetInformationalVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3);
        }

        int metadataIndex = informational.IndexOf('+');
        return metadataIndex >= 0 ? informational[..metadataIndex] : informational;
    }
}
