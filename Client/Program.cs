using Client.Commands;
using Client.Console;
using Client.Utils;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.RegularExpressions;
using LogLevel = Client.Utils.LogLevel;

namespace Client;

internal abstract partial class Program
{
    private static Logger? _logger;
    private static IConfiguration? _config;
    
    private static async Task Main()
    {
        try
        {
            System.Console.WriteLine("Starting Chunky Client application - initializing...");
            
            _config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            
            var logLevelStr = _config.GetValue<string>("Logging:MinimumLevel", "");
            var logToConsole = _config.GetValue("Logging:LogToConsole", true);
            var logToFile = _config.GetValue("Logging:LogToFile", true);
            var logFilePath = _config.GetValue<string>("Logging:LogFilePath", "logs/client.log");
            
            Utils.LogLevel logLevel;
            if (!string.IsNullOrEmpty(logLevelStr) && Enum.TryParse<LogLevel>(logLevelStr, true, out var configLevel))
            {
                logLevel = configLevel;
                System.Console.WriteLine($"Using log level from configuration: {logLevel}");
            }
            else
            {
                #if DEBUG
                logLevel = LogLevel.Debug;
                #else
                logLevel = LogLevel.Info;
                #endif
                System.Console.WriteLine($"Using default log level: {logLevel}");
            }
            
            System.Console.WriteLine($"Logging to console: {logToConsole}, Logging to file: {logToFile}, File path: {logFilePath}");
            
            _logger = new Logger(logLevel, logFilePath, logToConsole, logToFile);
            
            try {
                AnsiConsole.Write(new FigletText("Chunky Client").Color(Color.Green));
                AnsiConsole.MarkupLine("[blue]====================================[/]");
            }
            catch (Exception ex) {
                _logger.Error($"Error creating decorated header: {ex.Message}");
                System.Console.WriteLine("=== CHUNKY CLIENT ===");
            }
            
            var serverAddress = _config.GetValue<string>("Server:Address", null!);
            if (string.IsNullOrEmpty(serverAddress))
            {
                serverAddress = AnsiConsole.Ask<string>("Server address:", "localhost");
            }
            else
            {
                _logger.Info($"Using server address from configuration: {serverAddress}");
                AnsiConsole.MarkupLine($"Server address: [cyan]{Markup.Escape(serverAddress)}[/]");
            }
            
            var serverPort = _config.GetValue<int>("Server:Port", 0);
            if (serverPort <= 0)
            {
                serverPort = AnsiConsole.Ask<int>("Server port:", 25599);
            }
            else
            {
                _logger.Info($"Using server port from configuration: {serverPort}");
                AnsiConsole.MarkupLine($"Server port: [cyan]{serverPort}[/]");
            }

            _logger.Info($"Connecting to server at {serverAddress}:{serverPort}");
            var client = new LicenseClient(serverAddress, serverPort);

            client.MessageReceived += OnMessageReceived;
            client.ConnectionStatusChanged += OnConnectionStatusChanged;

            var username = AnsiConsole.Ask<string>("Username:", "User");
            var licenseKey = AnsiConsole.Prompt(
                new TextPrompt<string>("License key:")
                    .PromptStyle("green")
                    .Secret());

            await AnsiConsole.Status()
                .Start("Connecting to server...", async ctx =>
                {
                    _logger.Info($"Attempting to connect with username: {username}");
                    var connected = await client.ConnectAsync(username, licenseKey);

                    if (!connected)
                    {
                        _logger.Error("Connection failed");
                        AnsiConsole.MarkupLine("[red]Connection failed. Please check your credentials.[/]");
                        return;
                    }
                });

            if (client.IsConnected)
            {
                _logger.Info("Successfully connected to server");
                
                // Initialize command handler
                var commandHandler = new ConsoleCommandHandler(_logger);
                
                // Register commands
                CommandRegistration.RegisterConsoleCommands(commandHandler, client, _logger);

                // Start command handler
                await commandHandler.StartAsync();
            }

            _logger.Info("Application shutting down");
            if (client.IsConnected)
            {
                await client.DisconnectAsync("User exited the application");
            }
            
            AnsiConsole.MarkupLine("[yellow]Press any key to exit...[/]");
            System.Console.ReadKey();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"CRITICAL ERROR: {ex.Message}");
            System.Console.WriteLine(ex.StackTrace);
            
            if (_logger != null)
            {
                _logger.Error($"Unhandled exception: {ex.Message}");
                if (ex.StackTrace != null) _logger.Error(ex.StackTrace);
            }
            
            System.Console.WriteLine("Press any key to exit...");
            System.Console.ReadKey();
        }
    }

    private static void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var message = e.Message;
        
        try
        {
            if (message.Type == "COMMAND" && message.Content.StartsWith("{"))
            {
                _logger?.Info("Received special command message from server");
                AnsiConsole.MarkupLine($"[blue][{Markup.Escape(message.Timestamp.ToString("HH:mm:ss"))}] Special message received from server.[/]");
                return;
            }
            
            _logger?.Debug($"Message received: {message.Type} from {message.Sender}");
            AnsiConsole.MarkupLine($"[{Markup.Escape(message.Timestamp.ToString("HH:mm:ss"))}] [cyan]{Markup.Escape(message.Sender)}:[/] {Markup.Escape(message.Content)}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error processing message: {ex.Message}");
            System.Console.WriteLine($"Message: {message.Sender}: {message.Content}");
        }
    }

    private static void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
    {
        try
        {
            if (e.IsConnected)
            {
                _logger?.Info($"Connection status changed: {e.Reason}");
                AnsiConsole.MarkupLine($"[green]Status: {Markup.Escape(e.Reason)}[/]");
            }
            else
            {
                _logger?.Warning($"Disconnected: {e.Reason}");
                AnsiConsole.MarkupLine($"[red]Status: {Markup.Escape(e.Reason)}[/]");
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error handling connection status: {ex.Message}");
            System.Console.WriteLine($"Connection status: {e.IsConnected}, Reason: {e.Reason}");
        }
    }

    [GeneratedRegex("""[\"].+?[\"]|[^ ]+""")]
    private static partial Regex MyRegex();
}