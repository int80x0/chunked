using Client.Commands;
using LicenseSystem.Services;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace Client;

internal abstract class Program
{
    private static async Task Main()
    {
        AnsiConsole.Write(new FigletText("Chunky Client").Color(Color.Green));
        AnsiConsole.MarkupLine("[blue]====================================[/]");

        var serverAddress = AnsiConsole.Ask<string>("Server-Adresse:", "localhost");
        var serverPort = AnsiConsole.Ask<int>("Server-Port:", 25599);

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
                var connected = await client.ConnectAsync(username, licenseKey);

                if (!connected)
                {
                    AnsiConsole.MarkupLine("[red]Verbindung fehlgeschlagen. Bitte überprüfe deine Anmeldedaten.[/]");
                    return;
                }
            });

        if (client.IsConnected)
        {
            // Erstelle die Command-Handler
            var downloadCommand = new DownloadCommand(client);

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

                string[] parts = Regex.Matches(input, @"[\""].+?[\""]|[^ ]+")
                    .Cast<Match>()
                    .Select(m => m.Value.Trim('"'))
                    .ToArray();

                if (parts.Length == 0)
                    continue;

                string commandName = parts[0].ToLower();
                string[] args = parts.Skip(1).ToArray();

                try
                {
                    switch (commandName)
                    {
                        case "/download":
                            await downloadCommand.ExecuteAsync(args);
                            break;

                        case "/list":
                            AnsiConsole.MarkupLine("[cyan]Verfügbare Spiele:[/]");
                            AnsiConsole.MarkupLine("  [green]20952[/] - Grand Theft Auto V");
                            AnsiConsole.MarkupLine("  [green]2282[/] - The Witcher 3: Wild Hunt");
                            break;

                        case "/help":
                            AnsiConsole.MarkupLine("[cyan]Hilfe:[/]");
                            AnsiConsole.MarkupLine("  [blue]/download <game_id>[/] - Lade ein Spiel herunter");
                            AnsiConsole.MarkupLine("  [blue]/list[/] - Zeige verfügbare Spiele");
                            AnsiConsole.MarkupLine("  [blue]/help[/] - Zeige Hilfe");
                            AnsiConsole.MarkupLine("  [blue]/exit[/] - Beende die Anwendung");
                            break;

                        case "/exit":
                            running = false;
                            await client.DisconnectAsync("Benutzer hat die Anwendung beendet");
                            break;

                        default:
                            AnsiConsole.MarkupLine("[yellow]Unbekannter Befehl. Gib '/help' ein, um alle verfügbaren Befehle anzuzeigen.[/]");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Fehler: {ex.Message}[/]");
                }
            }
        }

        AnsiConsole.MarkupLine("[yellow]Drücke eine Taste zum Beenden...[/]");
        Console.ReadKey();
    }

    private static void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        var message = e.Message;
        
        // Prüfe, ob es sich um eine spezielle JSON-Nachricht handelt
        if (message.Type == "COMMAND" && message.Content.StartsWith("{"))
        {
            // Hier würde die Verarbeitung von JSON-Nachrichten erfolgen
            // z.B. für Download-Informationen
            AnsiConsole.MarkupLine($"[blue][{message.Timestamp:HH:mm:ss}] Spezielle Nachricht vom Server erhalten.[/]");
            return;
        }
        
        // Standard-Nachrichten
        AnsiConsole.MarkupLine($"[{message.Timestamp:HH:mm:ss}] [cyan]{message.Sender}:[/] {message.Content}");
    }

    private static void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
    {
        if (e.IsConnected)
        {
            AnsiConsole.MarkupLine($"[green]Status: {e.Reason}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Status: {e.Reason}[/]");
        }
    }
}