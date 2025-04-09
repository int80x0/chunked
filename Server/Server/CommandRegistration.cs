using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Commands;
using Server.Console;
using Server.Game;
using Server.Utils;

namespace Server.Server
{
    public static class CommandRegistration
    {
        public static void RegisterConsoleCommands(
            IServiceProvider services,
            ConsoleCommandHandler commandHandler)
        {
            var logger = services.GetRequiredService<Logger>();
            logger.Info("Registriere Console-Befehle...");
            
            try
            {
                // Basis-Befehle
                var broadcastCommand = new BroadcastCommand(
                    services.GetRequiredService<LicenseSystem.Services.LicenseServer>(),
                    services.GetRequiredService<Logger>());
                
                var usersCommand = new UsersCommand(
                    services.GetRequiredService<LicenseSystem.Services.LicenseServer>(),
                    services.GetRequiredService<Logger>());
                
                var kickCommand = new KickCommand(
                    services.GetRequiredService<LicenseSystem.Services.LicenseServer>(),
                    services.GetRequiredService<Logger>());
                
                var extendCommand = new ExtendCommand(
                    services.GetRequiredService<LicenseSystem.Services.LicenseServer>(),
                    services.GetRequiredService<Logger>());
                
                // Game-Befehle
                var uploadCommand = new UploadCommand(
                    services.GetRequiredService<DiscordSocketClient>(),
                    services.GetRequiredService<IConfiguration>(),
                    services.GetRequiredService<Logger>(),
                    services.GetRequiredService<GameInfoService>(),
                    services.GetRequiredService<ChunkManager>());
                
                var downloadCommand = new DownloadCommand(
                    services.GetRequiredService<IConfiguration>(),
                    services.GetRequiredService<Logger>(),
                    services.GetRequiredService<LicenseSystem.Services.LicenseServer>(),
                    services.GetRequiredService<GameInfoService>(),
                    services.GetRequiredService<DiscordSocketClient>());
                
                var versionCommand = new VersionCommand(
                    services.GetRequiredService<DiscordSocketClient>(),
                    services.GetRequiredService<IConfiguration>(),
                    services.GetRequiredService<Logger>(),
                    services.GetRequiredService<GameInfoService>());
                
                // Neuer UpdateCommand
                var updateCommand = new UpdateCommand(
                    services.GetRequiredService<DiscordSocketClient>(),
                    services.GetRequiredService<IConfiguration>(),
                    services.GetRequiredService<Logger>(),
                    services.GetRequiredService<GameInfoService>(),
                    services.GetRequiredService<ChunkManager>());
                
                // Registriere alle Befehle
                commandHandler.RegisterCommand(broadcastCommand);
                commandHandler.RegisterCommand(usersCommand);
                commandHandler.RegisterCommand(kickCommand);
                commandHandler.RegisterCommand(extendCommand);
                commandHandler.RegisterCommand(uploadCommand);
                commandHandler.RegisterCommand(downloadCommand);
                commandHandler.RegisterCommand(versionCommand);
                commandHandler.RegisterCommand(updateCommand); // Registriere den neuen UpdateCommand
                
                logger.Info($"Insgesamt {8} Console-Befehle registriert."); // Update die Zahl
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Registrieren der Console-Befehle: {ex.Message}");
            }
        }
    }
    
    // Basis-Befehle
    
    public class BroadcastCommand : ConsoleCommand
    {
        private readonly LicenseSystem.Services.LicenseServer _licenseServer;
        private readonly Logger _logger;

        public BroadcastCommand(
            LicenseSystem.Services.LicenseServer licenseServer,
            Logger logger) 
            : base(
                "broadcast", 
                "Sendet eine Nachricht an alle verbundenen Clients", 
                "<nachricht>", 
                "broadcast \"Serverupdate in 10 Minuten, bitte speichern!\"")
        {
            _licenseServer = licenseServer;
            _logger = logger;
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                WriteError("Du musst eine Nachricht angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string message = string.Join(" ", args);
            
            try
            {
                _licenseServer.BroadcastMessage(message);
                WriteSuccess($"Nachricht an alle Clients gesendet: \"{message}\"");
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Senden der Broadcast-Nachricht: {ex.Message}");
                _logger.Error($"Fehler beim Broadcast-Command: {ex}");
            }
        }
    }
    
    public class UsersCommand : ConsoleCommand
    {
        private readonly LicenseSystem.Services.LicenseServer _licenseServer;
        private readonly Logger _logger;

        public UsersCommand(
            LicenseSystem.Services.LicenseServer licenseServer,
            Logger logger) 
            : base(
                "users", 
                "Zeigt alle Benutzer an", 
                "[online]", 
                "users online")
        {
            _licenseServer = licenseServer;
            _logger = logger;
        }

        public override async Task ExecuteAsync(string[] args)
        {
            bool onlineOnly = args.Length > 0 && args[0].ToLower() == "online";
            
            try
            {
                var users = onlineOnly ? _licenseServer.GetOnlineUsers() : _licenseServer.GetAllUsers();
                
                if (users.Count == 0)
                {
                    WriteInfo(onlineOnly ? "Keine Benutzer online." : "Keine Benutzer registriert.");
                    return;
                }
                
                WriteInfo($"{users.Count} Benutzer {(onlineOnly ? "online" : "registriert")}:");
                System.Console.WriteLine();
                
                // Tabellenkopf
                System.Console.WriteLine($"{"Benutzername",-20} | {"Status",-10} | {"Lizenz",-20} | {"Gültig bis",-20} | {"IP-Adresse",-15}");
                System.Console.WriteLine(new string('-', 95));
                
                foreach (var user in users)
                {
                    string status = user.IsOnline ? "Online" : "Offline";
                    string validUntil = user.LicenseExpiration.ToString("dd.MM.yyyy HH:mm");
                    
                    System.Console.WriteLine(
                        $"{user.Username,-20} | {status,-10} | {user.LicenseKey,-20} | {validUntil,-20} | {user.IP,-15}");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Abrufen der Benutzer: {ex.Message}");
                _logger.Error($"Fehler beim Users-Command: {ex}");
            }
        }
    }
    
    public class KickCommand : ConsoleCommand
    {
        private readonly LicenseSystem.Services.LicenseServer _licenseServer;
        private readonly Logger _logger;

        public KickCommand(
            LicenseSystem.Services.LicenseServer licenseServer,
            Logger logger) 
            : base(
                "kick", 
                "Trennt einen Benutzer vom Server", 
                "<benutzername> [grund]", 
                "kick MaxMustermann \"Unerlaubte Aktivität\"")
        {
            _licenseServer = licenseServer;
            _logger = logger;
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                WriteError("Du musst einen Benutzernamen angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string username = args[0];
            string reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Vom Administrator getrennt";
            
            try
            {
                var users = _licenseServer.GetAllUsers();
                var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                
                if (user == null)
                {
                    WriteError($"Benutzer '{username}' nicht gefunden.");
                    return;
                }
                
                if (!user.IsOnline)
                {
                    WriteError($"Benutzer '{username}' ist nicht online.");
                    return;
                }
                
                _licenseServer.DisconnectClient(user.ClientId, reason);
                WriteSuccess($"Benutzer '{username}' wurde vom Server getrennt. Grund: {reason}");
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Trennen des Benutzers: {ex.Message}");
                _logger.Error($"Fehler beim Kick-Command: {ex}");
            }
        }
    }
    
    public class ExtendCommand : ConsoleCommand
    {
        private readonly LicenseSystem.Services.LicenseServer _licenseServer;
        private readonly Logger _logger;

        public ExtendCommand(
            LicenseSystem.Services.LicenseServer licenseServer,
            Logger logger) 
            : base(
                "extend", 
                "Verlängert eine Lizenz", 
                "<lizenzschlüssel> <tage>", 
                "extend LICS-ABCD-1234-5678 30")
        {
            _licenseServer = licenseServer;
            _logger = logger;
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 2)
            {
                WriteError("Du musst einen Lizenzschlüssel und die Anzahl der Tage angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string licenseKey = args[0];
            
            if (!int.TryParse(args[1], out int days) || days <= 0)
            {
                WriteError("Die Anzahl der Tage muss eine positive Zahl sein.");
                return;
            }
            
            try
            {
                bool result = _licenseServer.ExtendLicense(licenseKey, days);
                
                if (result)
                {
                    var users = _licenseServer.GetAllUsers();
                    var user = users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                    
                    if (user != null)
                    {
                        WriteSuccess($"Die Lizenz für Benutzer '{user.Username}' wurde um {days} Tage verlängert.");
                        WriteInfo($"Neues Ablaufdatum: {user.LicenseExpiration:dd.MM.yyyy HH:mm}");
                    }
                    else
                    {
                        WriteSuccess($"Die Lizenz '{licenseKey}' wurde um {days} Tage verlängert.");
                    }
                }
                else
                {
                    WriteError($"Die Lizenz '{licenseKey}' konnte nicht verlängert werden.");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Verlängern der Lizenz: {ex.Message}");
                _logger.Error($"Fehler beim Extend-Command: {ex}");
            }
        }
    }
}