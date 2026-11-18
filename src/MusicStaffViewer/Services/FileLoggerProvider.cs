using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Com.Example.MusicStaffViewer.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 30;
    private readonly string _directory;
    private readonly Channel<string> _messages = Channel.CreateBounded<string>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly Task _writer;

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        DeleteExpiredLogs();
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        _messages.Writer.TryComplete();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
    }

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        string? currentDay = null;
        try
        {
            await foreach (string message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    string day = DateTime.Now.ToString("yyyyMMdd");
                    if (writer is null || currentDay != day)
                    {
                        if (writer is not null)
                        {
                            await writer.DisposeAsync().ConfigureAwait(false);
                        }

                        currentDay = day;
                        var stream = new FileStream(Path.Combine(_directory, $"app-{day}.log"),
                            FileMode.Append, FileAccess.Write, FileShare.Read, 16384, useAsync: true);
                        writer = new StreamWriter(stream) { AutoFlush = true };
                    }

                    await writer.WriteLineAsync(message).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void DeleteExpiredLogs()
    {
        try
        {
            DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (string file in Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider._messages.Writer.TryWrite(line);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}
