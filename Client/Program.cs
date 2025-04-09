using LicenseSystem.Services;

namespace Client;

internal abstract class Program
{
    private static async Task Main()
    {
        Console.WriteLine("License Client");
        Console.WriteLine("==============");

        const string serverAddress = "localhost";
        const int serverPort = 25599;

        var client = new LicenseClient(serverAddress, serverPort);

        client.MessageReceived += OnMessageReceived;
        client.ConnectionStatusChanged += OnConnectionStatusChanged;

        Console.Write("Benutzername: ");
        var username = Console.ReadLine();

        Console.Write("Lizenzschlüssel: ");
        var licenseKey = Console.ReadLine();

        Console.WriteLine("Verbinde zum Server...");
        var connected = await client.ConnectAsync(username, licenseKey);

        if (connected)
        {
            Console.WriteLine("Verbunden! Verfügbare Befehle:");
            Console.WriteLine("  /send <nachricht> - Sende eine Nachricht (Befehl) an den Server");
            Console.WriteLine("  /exit - Beende die Anwendung");
            Console.WriteLine();

            var running = true;
            while (running && client.IsConnected)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrEmpty(input))
                    continue;

                if (input.StartsWith("/send "))
                {
                    var command = input[6..];
                    await client.SendCommandAsync(command);
                }
                else if (input == "/exit")
                {
                    running = false;
                    await client.DisconnectAsync("Benutzer hat die Anwendung beendet");
                }
                else
                {
                    Console.WriteLine("Unbekannter Befehl. Verfügbare Befehle: /send, /exit");
                }
            }
        }

        Console.WriteLine("Drücke eine Taste zum Beenden...");
        Console.ReadKey();
    }

    private static void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        var message = e.Message;
        Console.WriteLine($"[{message.Timestamp:HH:mm:ss}] {message.Sender}: {message.Content}");
    }

    private static void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
    {
        Console.ForegroundColor = e.IsConnected ? ConsoleColor.Green : ConsoleColor.Red;

        Console.WriteLine($"Status: {e.Reason}");

        Console.ResetColor();
    }
}