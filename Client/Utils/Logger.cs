using System.Text;
using Spectre.Console;

namespace Client.Utils
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class Logger
    {
        private readonly string _logFilePath;
        private readonly LogLevel _minLevel;

        public Logger(string logFilePath = "logs/client.log", bool logToConsole = true, bool logToFile = true)
        {
            _logFilePath = logFilePath;
            LogToConsole = logToConsole;
            LogToFile = logToFile;

#if DEBUG
            _minLevel = LogLevel.Debug;
#else
            _minLevel = LogLevel.Info;
#endif

            if (LogToFile)
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
            }

            System.Console.WriteLine(
                $"DIAGNOSTIC: Logger initialized with Level: {_minLevel}, Console: {LogToConsole}, File: {LogToFile}");
        }

        public Logger(LogLevel minLevel, string logFilePath = "logs/client.log", bool logToConsole = true,
            bool logToFile = true)
        {
            _minLevel = minLevel;
            _logFilePath = logFilePath;
            LogToConsole = logToConsole;
            LogToFile = logToFile;

            if (LogToFile)
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
            }

            System.Console.WriteLine(
                $"DIAGNOSTIC: Logger initialized with Level: {_minLevel}, Console: {LogToConsole}, File: {LogToFile}");
        }

        private bool LogToConsole { get; }

        private bool LogToFile { get; }

        public void Debug(string message)
        {
            if (_minLevel <= LogLevel.Debug)
            {
                LogMessage(LogLevel.Debug, message);
            }
        }

        public void Info(string message)
        {
            if (_minLevel <= LogLevel.Info)
            {
                LogMessage(LogLevel.Info, message);
            }
        }

        public void Warning(string message)
        {
            if (_minLevel <= LogLevel.Warning)
            {
                LogMessage(LogLevel.Warning, message);
            }
        }

        public void Error(string message)
        {
            if (_minLevel <= LogLevel.Error)
            {
                LogMessage(LogLevel.Error, message);
            }
        }

        private void LogMessage(LogLevel level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var formattedMessage = $"[{timestamp}] [{level.ToString().ToUpper()}] {message}";

            if (LogToConsole)
            {
                WriteToConsole(level, formattedMessage);
            }

            if (LogToFile)
            {
                Task.Run(() => WriteToFileAsync(formattedMessage));
            }
        }

        private void WriteToConsole(LogLevel level, string message)
        {
            if (!LogToConsole) return;

            try
            {
                var escapedMessage = Markup.Escape(message);

                var coloredMessage = level switch
                {
                    LogLevel.Debug => $"[gray]{escapedMessage}[/]",
                    LogLevel.Info => $"[green]{escapedMessage}[/]",
                    LogLevel.Warning => $"[yellow]{escapedMessage}[/]",
                    LogLevel.Error => $"[red]{escapedMessage}[/]",
                    _ => escapedMessage
                };

                AnsiConsole.MarkupLine(coloredMessage);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Markup error: {ex.Message}");
                System.Console.WriteLine(message);
            }
        }

        private async Task WriteToFileAsync(string message)
        {
            if (!LogToFile) return;

            try
            {
                message += Environment.NewLine;

                await using var fileStream =
                    new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                await writer.WriteAsync(message);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }
    }
}