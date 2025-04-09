using System.Net;
using System.Text.Json;
using LicenseSystem.Models;
using LicenseSystem.Services;

namespace Server;

internal abstract class Program_OLD
{
    private static void Maind()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        Console.WriteLine("Lizenz-Server wird gestartet...");

        int port = 25599; // Standard-Port
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

                    case ConsoleKey.L:
                        var onlineUsers = server.GetOnlineUsers();
                        Console.WriteLine($"Online-Benutzer ({onlineUsers.Count}):");
                        foreach (var user in onlineUsers)
                        {
                            Console.WriteLine($"- {user.Username} (Lizenz läuft ab am {user.LicenseExpiration})");
                        }
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
                            Console.WriteLine(result ? "Lizenz erfolgreich verlängert." : "Lizenzschlüssel nicht gefunden.");
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

    private static void CreateTestLicense()
    {
        // Pfad zur JSON-Datei
        string usersFilePath = "users.json";

        // Erstelle einen Testbenutzer
        var testUser = new User
        {
            Username = "TestUser",
            IP = "127.0.0.1",
            FirstLogin = DateTime.Now,
            LastLogin = DateTime.Now,
            LicenseKey = "LICS-ABCD-1234-5678", // Ein Test-Lizenzschlüssel
            LicenseExpiration = DateTime.Now.AddDays(30), // 30 Tage gültig
            RateLimit = 100,
            IsOnline = false,
            ClientId = null
        };

        // Liste erstellen oder vorhandene laden
        List<User> users = [];
        if (File.Exists(usersFilePath))
        {
            try
            {
                string json = File.ReadAllText(usersFilePath);
                users = JsonSerializer.Deserialize<List<User>>(json);
            }
            catch
            {
                // Bei Fehler neue Liste erstellen
                users = [];
            }
        }

        // Überprüfe, ob der Benutzer oder der Lizenzschlüssel bereits existiert
        var existingUser = users.FirstOrDefault(u => u.LicenseKey == testUser.LicenseKey);
        if (existingUser == null)
        {
            // Füge den Testbenutzer zur Liste hinzu
            users.Add(testUser);

            // Speichere die aktualisierte Liste
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string updatedJson = JsonSerializer.Serialize(users, options);
            File.WriteAllText(usersFilePath, updatedJson);

            Console.WriteLine("Testbenutzer erstellt mit Lizenzschlüssel: LICS-ABCD-1234-5678");
        }
        else
        {
            Console.WriteLine("Testbenutzer mit diesem Lizenzschlüssel existiert bereits");
        }
    }
}