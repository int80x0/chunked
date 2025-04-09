using LicenseSystem.Models;
using LicenseSystem.Services;

namespace LicenseSystem.Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("License Client");
            Console.WriteLine("==============");

            string serverAddress = "localhost";
            int serverPort = 5000;

            // Erstelle Client-Instanz
            LicenseClient client = new LicenseClient(serverAddress, serverPort);

            // Registriere Event-Handler
            client.MessageReceived += OnMessageReceived;
            client.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Benutzer nach Anmeldedaten fragen
            Console.Write("Benutzername: ");
            string username = Console.ReadLine();

            Console.Write("Lizenzschlüssel: ");
            string licenseKey = Console.ReadLine();

            Console.WriteLine("Verbinde zum Server...");
            bool connected = await client.ConnectAsync(username, licenseKey);

            if (connected)
            {
                Console.WriteLine("Verbunden! Verfügbare Befehle:");
                Console.WriteLine("  /send <nachricht> - Sende eine Nachricht (Befehl) an den Server");
                Console.WriteLine("  /exit - Beende die Anwendung");
                Console.WriteLine();

                // Hauptschleife für Benutzereingaben
                bool running = true;
                while (running && client.IsConnected)
                {
                    string input = Console.ReadLine();
                    if (string.IsNullOrEmpty(input))
                        continue;

                    if (input.StartsWith("/send "))
                    {
                        string command = input.Substring(6);
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

        static void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Message message = e.Message;
            Console.WriteLine($"[{message.Timestamp:HH:mm:ss}] {message.Sender}: {message.Content}");
        }

        // Event-Handler für Verbindungsstatus
        static void OnConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            if (e.IsConnected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Status: {e.Reason}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Status: {e.Reason}");
            }

            Console.ResetColor();
        }
    }
}