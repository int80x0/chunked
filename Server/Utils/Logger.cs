using System.Text;
using Microsoft.Extensions.Configuration;

namespace Server.Utils
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
        private readonly Lock _lock = new();
        private readonly string _logFilePath;
        private readonly LogLevel _minLevel;
        private readonly bool _logToConsole;
        private readonly bool _logToFile;

        private static readonly ConsoleColor[] LevelColors =
        [
            ConsoleColor.Gray,
            ConsoleColor.Green,
            ConsoleColor.Yellow,
            ConsoleColor.Red
        ];

        public Logger(IConfiguration config = null)
        {
            if (config != null)
            {
                _logToConsole = config.GetValue("Logging:LogToConsole", true);
                _logToFile = config.GetValue("Logging:LogToFile", true);
                _logFilePath = config.GetValue<string>("Logging:LogFilePath", "logs/chunkybot.log");

                var configLevel = config.GetValue<string>("Logging:MinimumLevel", "Info");
                _minLevel = Enum.TryParse<LogLevel>(configLevel, true, out var level) ? level : LogLevel.Info;
            }
            else
            {
                _logToConsole = true;
                _logToFile = true;
                _logFilePath = "logs/chunkybot.log";
                _minLevel = LogLevel.Info;
            }

            if (_logToFile)
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
            }
            
            
            System.Console.WriteLine($"DIAGNOSTIC: Logger initialized with Level: {_minLevel}, Console: {_logToConsole}, File: {_logToFile}");
        }
        
        
        public LogLevel MinimumLevel => _minLevel;
        
        
        public bool LogToConsole => _logToConsole;
        public bool LogToFile => _logToFile;

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

            if (_logToConsole)
            {
                WriteToConsole(level, formattedMessage);
            }

            if (_logToFile)
            {
                Task.Run(() => WriteToFileAsync(formattedMessage));
            }
        }

        private void WriteToConsole(LogLevel level, string message)
        {
            
            if (!_logToConsole) return;
            
            lock (_lock)
            {
                var oldColor = System.Console.ForegroundColor;
                System.Console.ForegroundColor = LevelColors[(int)level];
                System.Console.WriteLine(message);
                System.Console.ForegroundColor = oldColor;
            }
        }

        private async Task WriteToFileAsync(string message)
        {
            
            if (!_logToFile) return;
            
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
                lock (_lock)
                {
                    var oldColor = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"Error writing to log file: {ex.Message}");
                    System.Console.ForegroundColor = oldColor;
                }
            }
        }
    }
}