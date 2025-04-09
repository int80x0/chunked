using Client.Commands;
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
            Console.WriteLine("Starting Chunky Client application - initializing...");
            
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
                Console.WriteLine($"Using log level from configuration: {logLevel}");
            }
            else
            {
                #if DEBUG
                logLevel = LogLevel.Debug;
                #else
                logLevel = LogLevel.Info;
                #endif
                Console.WriteLine($"Using default log level: {logLevel}");
            }
            
            Console.WriteLine($"Logging to console: {logToConsole}, Logging to file: {logToFile}, File path: {logFilePath}");
            
            _logger = new Logger(logLevel, logFilePath, logToConsole, logToFile);
            
            try {
                AnsiConsole.Write(new FigletText("Chunky Client").Color(Color.Green));
                AnsiConsole.MarkupLine("[blue]====================================[/]");
            }
            catch (Exception ex) {
                _logger.Error($"Error creating decorated header: {ex.Message}");
                Console.WriteLine("=== CHUNKY CLIENT ===");
            }
            
            var serverAddress = _config.GetValue<string>("Server:Address", null!);
            if (string.IsNullOrEmpty(serverAddress))
            {
                serverAddress = AnsiConsole.Ask<string>("Server-Adresse:", "localhost");
            }
            else
            {
                _logger.Info($"Using server address from configuration: {serverAddress}");
                AnsiConsole.MarkupLine($"Server address: [cyan]{Markup.Escape(serverAddress)}[/]");
            }
            
            var serverPort = _config.GetValue<int>("Server:Port", 0);
            if (serverPort <= 0)
            {
                serverPort = AnsiConsole.Ask<int>("Server-Port:", 25599);
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

            var username = AnsiConsole.Ask<string>("Benutzername:", "User");
            var licenseKey = AnsiConsole.Prompt(
                new TextPrompt<string>("Lizenzschlüssel:")
                    .PromptStyle("green")
                    .Secret());

            await AnsiConsole.Status()
                .Start("Verbinde zum Server...", async ctx =>
                {
                    _logger.Info($"Attempting to connect with username: {username}");
                    var connected = await client.ConnectAsync(username, licenseKey);

                    if (!connected)
                    {
                        _logger.Error("Connection failed");
                        AnsiConsole.MarkupLine("[red]Verbindung fehlgeschlagen. Bitte überprüfe deine Anmeldedaten.[/]");
                        return;
                    }
                });

            if (client.IsConnected)
            {
                _logger.Info("Successfully connected to server");
                
                var downloadCommand = new DownloadCommand(client, _logger);

                AnsiConsole.MarkupLine("[green]Verbunden![/] Verfügbare Befehle:");
                AnsiConsole.MarkupLine("  [blue]/download <game_id>[/] - Lade ein Spiel herunter");
                AnsiConsole.MarkupLine("  [blue]/list[/] - Zeige verfügbare Spiele");
                AnsiConsole.MarkupLine("  [blue]/help[/] - Zeige Hilfe");
                AnsiConsole.MarkupLine("  [blue]/exit[/] - Beende die Anwendung");
                AnsiConsole.WriteLine();

                var running = true;
                while (running && client.IsConnected)
                {
                    var input = AnsiConsole.Ask<string>(">");

                    if (string.IsNullOrEmpty(input))
                        continue;

                    var parts = MyRegex().Matches(input)
                        .Cast<Match>()
                        .Select(m => m.Value.Trim('"'))
                        .ToArray();

                    if (parts.Length == 0)
                        continue;

                    var commandName = parts[0].ToLower();
                    var args = parts.Skip(1).ToArray();

                    try
                    {
                        _logger.Debug($"Processing command: {commandName} with {args.Length} arguments");
                        
                        switch (commandName)
                        {
                            case "/download":
                                await downloadCommand.ExecuteAsync(args);
                                break;

                            case "/list":
                                _logger.Info("Listing available games");
                                AnsiConsole.MarkupLine("[cyan]Verfügbare Spiele:[/]");
                                AnsiConsole.MarkupLine("  [green]20952[/] - Grand Theft Auto V");
                                AnsiConsole.MarkupLine("  [green]2282[/] - The Witcher 3: Wild Hunt");
                                break;

                            case "/help":
                                _logger.Debug("Showing help menu");
                                AnsiConsole.MarkupLine("[cyan]Hilfe:[/]");
                                AnsiConsole.MarkupLine("  [blue]/download <game_id>[/] - Lade ein Spiel herunter");
                                AnsiConsole.MarkupLine("  [blue]/list[/] - Zeige verfügbare Spiele");
                                AnsiConsole.MarkupLine("  [blue]/help[/] - Zeige Hilfe");
                                AnsiConsole.MarkupLine("  [blue]/exit[/] - Beende die Anwendung");
                                break;

                            case "/exit":
                                _logger.Info("Exiting application");
                                running = false;
                                await client.DisconnectAsync("Benutzer hat die Anwendung beendet");
                                break;

                            default:
                                _logger.Warning($"Unknown command: {commandName}");
                                AnsiConsole.MarkupLine("[yellow]Unbekannter Befehl. Gib '/help' ein, um alle verfügbaren Befehle anzuzeigen.[/]");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error executing command: {ex.Message}");
                        AnsiConsole.MarkupLine($"[red]Fehler: {ex.Message}[/]");
                    }
                }
            }

            _logger.Info("Application shutting down");
            AnsiConsole.MarkupLine("[yellow]Drücke eine Taste zum Beenden...[/]");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            // Catch any unhandled exceptions and log them
            Console.WriteLine($"CRITICAL ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            
            if (_logger != null)
            {
                _logger.Error($"Unhandled exception: {ex.Message}");
                if (ex.StackTrace != null) _logger.Error(ex.StackTrace);
            }
            
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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
            
            // Standard messages
            _logger?.Debug($"Message received: {message.Type} from {message.Sender}");
            AnsiConsole.MarkupLine($"[{Markup.Escape(message.Timestamp.ToString("HH:mm:ss"))}] [cyan]{Markup.Escape(message.Sender)}:[/] {Markup.Escape(message.Content)}");
        }
        catch (Exception ex)
        {
            _logger?.Error($"Error processing message: {ex.Message}");
            Console.WriteLine($"Message: {message.Sender}: {message.Content}");
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
            Console.WriteLine($"Connection status: {e.IsConnected}, Reason: {e.Reason}");
        }
    }

    [GeneratedRegex("""[\"].+?[\"]|[^ ]+""")]
    private static partial Regex MyRegex();
}