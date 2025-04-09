using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using LicenseSystem.Models;

namespace LicenseSystem.KeyGenerator
{
    class Program
    {
        private static readonly string KeysFile = "license_keys.json";
        private static List<LicenseKey> _licenseKeys;

        static void Main(string[] args)
        {
            Console.WriteLine("License Key Generator");
            Console.WriteLine("====================");

            LoadKeys();

            bool running = true;
            while (running)
            {
                Console.WriteLine("\nMenü:");
                Console.WriteLine("1. Neuen Lizenzschlüssel generieren");
                Console.WriteLine("2. Alle Lizenzschlüssel anzeigen");
                Console.WriteLine("3. Lizenzschlüssel suchen");
                Console.WriteLine("4. Lizenzschlüssel löschen");
                Console.WriteLine("5. Beenden");
                Console.Write("\nAuswahl: ");

                string choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        GenerateNewKey();
                        break;
                    case "2":
                        ListAllKeys();
                        break;
                    case "3":
                        SearchKey();
                        break;
                    case "4":
                        DeleteKey();
                        break;
                    case "5":
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Ungültige Auswahl. Bitte versuche es erneut.");
                        break;
                }
            }
        }

        // Laden der gespeicherten Lizenzschlüssel
        private static void LoadKeys()
        {
            if (File.Exists(KeysFile))
            {
                string json = File.ReadAllText(KeysFile);
                _licenseKeys = JsonSerializer.Deserialize<List<LicenseKey>>(json);
                Console.WriteLine($"{_licenseKeys.Count} Lizenzschlüssel geladen.");
            }
            else
            {
                _licenseKeys = new List<LicenseKey>();
                Console.WriteLine("Keine Lizenzschlüssel vorhanden. Neue Datei wird erstellt.");
            }
        }

        // Speichern der Lizenzschlüssel
        private static void SaveKeys()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(_licenseKeys, options);
            File.WriteAllText(KeysFile, json);
            Console.WriteLine("Lizenzschlüssel gespeichert.");
        }

        // Generieren eines neuen Lizenzschlüssels
        private static void GenerateNewKey()
        {
            Console.Write("Gültigkeitsdauer in Tagen (Standard: 30): ");
            string input = Console.ReadLine();
            int days = 30;
            int.TryParse(input, out days);

            Console.Write("Rate Limit (Anfragen pro Stunde, Standard: 100): ");
            input = Console.ReadLine();
            int rateLimit = 100;
            int.TryParse(input, out rateLimit);

            string key = GenerateLicenseKey();
            
            var licenseKey = new LicenseKey
            {
                Key = key,
                GeneratedDate = DateTime.Now,
                ExpirationDate = DateTime.Now.AddDays(days),
                IsUsed = false,
                AssignedTo = null,
                RateLimit = rateLimit
            };

            _licenseKeys.Add(licenseKey);
            SaveKeys();

            Console.WriteLine("\nNeuer Lizenzschlüssel generiert:");
            Console.WriteLine($"Schlüssel: {key}");
            Console.WriteLine($"Gültig bis: {licenseKey.ExpirationDate:dd.MM.yyyy}");
            Console.WriteLine($"Rate Limit: {licenseKey.RateLimit} Anfragen/Stunde");
        }

        // Generieren eines zufälligen Lizenzschlüssels
        private static string GenerateLicenseKey()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[10];
            rng.GetBytes(bytes);
                
            var base64 = Convert.ToBase64String(bytes);
            base64 = base64.Replace("/", "A").Replace("+", "B").Replace("=", "");
                
            return $"LICS-{base64[..4]}-{base64.Substring(4, 4)}-{base64.Substring(8, 4)}";
        }

        // Anzeigen aller Lizenzschlüssel
        private static void ListAllKeys()
        {
            if (_licenseKeys.Count == 0)
            {
                Console.WriteLine("Keine Lizenzschlüssel vorhanden.");
                return;
            }

            Console.WriteLine("\nAlle Lizenzschlüssel:");
            Console.WriteLine("=====================");
            Console.WriteLine("Nr | Schlüssel        | Gültig bis  | Status | Zugewiesen an");
            Console.WriteLine("---+------------------+-------------+--------+--------------");

            for (int i = 0; i < _licenseKeys.Count; i++)
            {
                var key = _licenseKeys[i];
                string status = key.IsUsed ? "Benutzt" : "Frei";
                string assignedTo = key.AssignedTo ?? "-";
                
                Console.WriteLine($"{i+1,2} | {key.Key} | {key.ExpirationDate:dd.MM.yyyy} | {status,-6} | {assignedTo}");
            }
        }

        // Suchen eines Lizenzschlüssels
        private static void SearchKey()
        {
            Console.Write("Gib den Lizenzschlüssel oder einen Teil davon ein: ");
            string searchTerm = Console.ReadLine().ToUpper();

            var results = _licenseKeys.FindAll(k => k.Key.Contains(searchTerm));
            
            if (results.Count == 0)
            {
                Console.WriteLine("Keine Lizenzschlüssel gefunden.");
                return;
            }

            Console.WriteLine($"\n{results.Count} Lizenzschlüssel gefunden:");
            Console.WriteLine("=====================");
            Console.WriteLine("Nr | Schlüssel        | Gültig bis  | Status | Zugewiesen an");
            Console.WriteLine("---+------------------+-------------+--------+--------------");

            for (int i = 0; i < results.Count; i++)
            {
                var key = results[i];
                string status = key.IsUsed ? "Benutzt" : "Frei";
                string assignedTo = key.AssignedTo ?? "-";
                
                Console.WriteLine($"{i+1,2} | {key.Key} | {key.ExpirationDate:dd.MM.yyyy} | {status,-6} | {assignedTo}");
            }
        }

        // Löschen eines Lizenzschlüssels
        private static void DeleteKey()
        {
            ListAllKeys();
            
            Console.Write("\nGib die Nummer des zu löschenden Lizenzschlüssels ein: ");
            if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= _licenseKeys.Count)
            {
                var key = _licenseKeys[index - 1];
                Console.WriteLine($"Lizenzschlüssel '{key.Key}' wird gelöscht.");
                
                Console.Write("Bist du sicher? (j/n): ");
                if (Console.ReadLine().ToLower() == "j")
                {
                    _licenseKeys.RemoveAt(index - 1);
                    SaveKeys();
                    Console.WriteLine("Lizenzschlüssel gelöscht.");
                }
                else
                {
                    Console.WriteLine("Löschvorgang abgebrochen.");
                }
            }
            else
            {
                Console.WriteLine("Ungültige Nummer. Vorgang abgebrochen.");
            }
        }
    }
}