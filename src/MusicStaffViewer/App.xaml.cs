using System.IO.Pipes;
using System.Text;
using Com.Example.MusicStaffViewer.Core.Models;
using Com.Example.MusicStaffViewer.Core.MusicXml;
using Com.Example.MusicStaffViewer.Core.Services;
using Com.Example.MusicStaffViewer.Rendering;
using Com.Example.MusicStaffViewer.ViewModels;
using Com.Example.MusicStaffViewer.Views;
using Com.Example.MusicStaffViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Com.Example.MusicStaffViewer;

public partial class App : Application
{
    private static readonly Lazy<string> OpenFilePipeName = new(CreatePipeName);
    private static readonly Lazy<ResourceLoader> SharedStrings = new(() => new ResourceLoader());
    private readonly IHost _host;
    private AppInstance? _instance;
    private CancellationTokenSource? _pipeCancellation;
    private Task? _pipeTask;
    public static MainWindow? MainWindow { get; private set; }
    public static IServiceProvider Services { get; private set; } = null!;
    public static string? StartupPath { get; set; }
    internal static AppSettings? InitialSettings { get; set; }
    internal static ResourceLoader Strings => SharedStrings.Value;

    public App()
    {
        try
        {
            InitializeComponent();
            UnhandledException += (_, e) => ReportFatal(e.Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ReportFatal(e.Exception);
                e.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    ReportFatal(exception);
                }
            };
            _host = new HostBuilder()
                .ConfigureLogging(logging =>
                {
                    logging.AddProvider(new FileLoggerProvider(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MusicStaffViewer", "logs")));
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ScoreFileReader>();
                    services.AddSingleton<ScoreMetadataReader>();
                    services.AddSingleton(_ => new UserSettingsService(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MusicStaffViewer", "settings.json")));
                    services.AddTransient<AlphaTabScoreSession>();
                    services.AddTransient<Func<AlphaTabScoreSession>>(provider =>
                        () => provider.GetRequiredService<AlphaTabScoreSession>());
                    services.AddSingleton<ScoreViewModel>();
                    services.AddSingleton<ScorePage>();
                    services.AddSingleton<SettingsPage>();
                    services.AddSingleton(provider =>
                        new Lazy<SettingsPage>(() => provider.GetRequiredService<SettingsPage>()));
                    services.AddSingleton<AboutPage>();
                    services.AddSingleton(provider =>
                        new Lazy<AboutPage>(() => provider.GetRequiredService<AboutPage>()));
                    services.AddSingleton<ThemeService>();
                    services.AddSingleton<MainWindow>();
                }).Build();
            Services = _host.Services;
        }
        catch (Exception exception)
        {
            ReportFatal(exception);
            throw;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppInstance instance = AppInstance.FindOrRegisterForKey("Com.Example.MusicStaffViewer");
            if (!instance.IsCurrent)
            {
                string? requestedPath = FindSupportedScore(Environment.GetCommandLineArgs().Skip(1));
                if (requestedPath is not null)
                {
                    await TrySendOpenPathAsync(requestedPath);
                }

                await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Exit();
                return;
            }

            _instance = instance;
            Task<AppSettings> loadSettingsTask = Services.GetRequiredService<UserSettingsService>().LoadAsync();
            _pipeCancellation = new CancellationTokenSource();
            _pipeTask = Task.Run(() => RunOpenPipeAsync(_pipeCancellation.Token));
            instance.Activated += (_, _) => MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                MainWindow?.AppWindow.Show();
                MainWindow?.Activate();
                try
                {
                    string? path = GetActivationPath(AppInstance.GetCurrent().GetActivatedEventArgs());
                    if (path is not null && MainWindow is not null)
                    {
                        await Services.GetRequiredService<ScorePage>().OpenPathAsync(path);
                    }
                }
                catch (Exception exception)
                {
                    Services.GetRequiredService<ILogger<App>>()
                        .LogError(exception, "Could not open the activated score file");
                }
            });
            await _host.StartAsync();
            StartupPath = GetActivationPath(AppInstance.GetCurrent().GetActivatedEventArgs()) ??
                FindSupportedScore(Environment.GetCommandLineArgs().Skip(1));
            AppSettings saved = await loadSettingsTask;
            InitialSettings = saved;
            MainWindow = Services.GetRequiredService<MainWindow>();
            Services.GetRequiredService<ThemeService>().Apply(saved, MainWindow.RootElement);
            MainWindow.RestoreWindow(saved.Window);
            MainWindow.Closed += async (_, _) =>
            {
                try
                {
                    await Services.GetRequiredService<ScoreViewModel>().DisposeAsync();
                    _pipeCancellation?.Cancel();
                    if (_pipeTask is not null)
                    {
                        try
                        {
                            await _pipeTask;
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    _pipeCancellation?.Dispose();
                    await _host.StopAsync();
                    _host.Dispose();
                }
                catch (Exception exception)
                {
                    ReportFatal(exception);
                }
            };
            MainWindow.Activate();
            Services.GetRequiredService<ScorePage>().EnsureInitialized();
        }
        catch (Exception exception)
        {
            ReportFatal(exception);
            throw;
        }
    }

    private static async Task RunOpenPipeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(OpenFilePipeName.Value, PipeDirection.In,
                    1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                string? path = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(path) || !IsSupportedScore(path))
                {
                    continue;
                }

                MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (MainWindow is not null)
                        {
                            await Services.GetRequiredService<ScorePage>().OpenPathAsync(path);
                        }
                    }
                    catch (Exception exception)
                    {
                        Services.GetRequiredService<ILogger<App>>()
                            .LogError(exception, "Could not open the score sent by another instance");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                Services.GetRequiredService<ILogger<App>>()
                    .LogError(exception, "The single-instance open pipe stopped");
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static async Task TrySendOpenPathAsync(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", OpenFilePipeName.Value, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false));
            await writer.WriteLineAsync(path).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Services.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not send the score file to the running instance");
        }
    }

    private static string? GetActivationPath(AppActivationArguments args)
    {
        try
        {
            if (args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
            {
                foreach (Windows.Storage.IStorageItem item in fileArgs.Files)
                {
                    if (item is Windows.Storage.StorageFile file && IsSupportedScore(file.Path))
                    {
                        return file.Path;
                    }
                }
            }

            if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
            {
                return FindSupportedScore(launch.Arguments);
            }
        }
        catch (Exception exception)
        {
            Services.GetRequiredService<ILogger<App>>()
                .LogWarning(exception, "Could not read the activated score file");
        }

        return null;
    }

    private static string? FindSupportedScore(string arguments)
    {
        string trimmed = arguments.Trim().Trim('"');
        if (IsSupportedScore(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        foreach (string segment in arguments.Split('"',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsSupportedScore(segment))
            {
                return Path.GetFullPath(segment);
            }
        }

        foreach (string token in arguments.Split([' ', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsSupportedScore(token))
            {
                return Path.GetFullPath(token);
            }
        }

        return null;
    }

    private static string? FindSupportedScore(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            string candidate = argument.Trim().Trim('"');
            if (IsSupportedScore(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool IsSupportedScore(string path) =>
        path.Length > 0 && File.Exists(path) &&
        ScoreFileReader.SupportedExtensions.Contains(Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase);

    private static void ReportFatal(Exception exception)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicStaffViewer", "logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "fatal.log");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 1024 * 1024)
            {
                string previous = path + ".1";
                File.Move(path, previous, overwrite: true);
            }

            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {exception}\n");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static Windows.UI.ViewManagement.UISettings SystemUISettings { get; } = new();

    public static bool IsSystemDark
    {
        get
        {
            Windows.UI.Color background = SystemUISettings
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return background.R + background.G + background.B < 384;
        }
    }

    private static string CreatePipeName()
    {
        string user = Environment.UserName.Replace('\\', '_').Replace('/', '_');
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"MusicStaffViewer.{user}.{process.SessionId}.OpenFile";
    }
}
