using LicenseSystem.Services;

namespace LicenseSystem.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Lizenz-Server wird gestartet...");

            int port = 5000; // Standard-Port
            string usersFilePath = "users.json"; // Standard-Pfad für Benutzerdaten

            // Erstelle Server-Instanz
            LicenseServer server = new LicenseServer(port, usersFilePath);
            server.Start();

            Console.WriteLine("Server läuft. Drücke 'Q' zum Beenden.");

            // Einfache Befehlsschleife für Serveradministration
            bool running = true;
            while (running)
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.Q:
                        running = false;
                        break;

                    case ConsoleKey.B:
                        Console.Write("Gib eine Broadcast-Nachricht ein: ");
                        string message = Console.ReadLine();
                        server.BroadcastMessage(message);
                        break;

                    case ConsoleKey.D:
                        Console.Write("Gib die Client-ID ein, die getrennt werden soll: ");
                        string clientId = Console.ReadLine();
                        Console.Write("Gib den Grund ein: ");
                        string reason = Console.ReadLine();
                        server.DisconnectClient(clientId, reason);
                        break;

                    case ConsoleKey.E:
                        Console.Write("Gib den Lizenzschlüssel ein, der verlängert werden soll: ");
                        string licenseKey = Console.ReadLine();
                        Console.Write("Gib die Anzahl der Tage ein: ");
                        if (int.TryParse(Console.ReadLine(), out int days))
                        {
                            bool result = server.ExtendLicense(licenseKey, days);
                            Console.WriteLine(result
                                ? "Lizenz erfolgreich verlängert."
                                : "Lizenzschlüssel nicht gefunden.");
                        }
                        else
                        {
                            Console.WriteLine("Ungültige Eingabe für Tage.");
                        }

                        break;

                    case ConsoleKey.H:
                        Console.WriteLine("\nVerfügbare Befehle:");
                        Console.WriteLine("Q - Server beenden");
                        Console.WriteLine("B - Broadcast-Nachricht senden");
                        Console.WriteLine("L - Online-Benutzer anzeigen");
                        Console.WriteLine("D - Client trennen");
                        Console.WriteLine("E - Lizenz verlängern");
                        Console.WriteLine("H - Hilfe anzeigen");
                        break;
                }
            }

            Console.WriteLine("Server wird beendet...");
            server.Stop();
        }
    }
}