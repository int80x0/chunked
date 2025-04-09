using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using LicenseSystem.Services;
using Server.Server;
using Server.Utils;

namespace Server.Commands
{
    [Group("admin", "Administrative Befehle")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public class AdminCommands(
        LicenseServer licenseServer,
        ServerHandler serverHandler,
        Logger logger)
        : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("users", "Zeigt alle registrierten Benutzer an")]
        public async Task UsersCommand()
        {
            logger.Info($"Command 'admin users' ausgeführt von {Context.User.Username}");
            
            try
            {
                var users = licenseServer.GetAllUsers();
                
                if (users.Count == 0)
                {
                    await RespondAsync("Keine Benutzer registriert.", ephemeral: true);
                    return;
                }
                
                var currentEmbed = new EmbedBuilder()
                    .WithTitle("Registrierte Benutzer")
                    .WithColor(Color.Blue)
                    .WithDescription($"Insgesamt {users.Count} Benutzer registriert.")
                    .WithCurrentTimestamp();
                
                var embeds = new List<Embed>();
                var fieldCount = 0;
                
                foreach (var user in users)
                {
                    if (fieldCount >= 25)
                    {
                        embeds.Add(currentEmbed.Build());
                        
                        currentEmbed = new EmbedBuilder()
                            .WithTitle("Registrierte Benutzer (Fortsetzung)")
                            .WithColor(Color.Blue)
                            .WithCurrentTimestamp();
                            
                        fieldCount = 0;
                    }
                    currentEmbed.AddField(
                        $"{user.Username} ({(user.IsOnline ? "🟢 Online" : "⚫ Offline")})",
                        $"Lizenz: `{user.LicenseKey}`\n" +
                        $"Gültig bis: {user.LicenseExpiration:dd.MM.yyyy}\n" +
                        $"IP: {user.IP}\n" +
                        $"Letzter Login: {user.LastLogin:dd.MM.yyyy HH:mm}"
                    );
                    
                    fieldCount++;
                }
                
                embeds.Add(currentEmbed.Build());
                
                await RespondAsync(embeds: embeds.ToArray(), ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin users' Commands: {ex.Message}");
                await RespondAsync("Beim Abrufen der Benutzer ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
        
        [SlashCommand("online", "Zeigt alle online Benutzer an")]
        public async Task OnlineCommand()
        {
            logger.Info($"Command 'admin online' ausgeführt von {Context.User.Username}");
            
            try
            {
                var onlineUsers = licenseServer.GetOnlineUsers();
                
                if (onlineUsers.Count == 0)
                {
                    await RespondAsync("Keine Benutzer sind derzeit online.", ephemeral: true);
                    return;
                }
                
                var embed = new EmbedBuilder()
                    .WithTitle("Online Benutzer")
                    .WithColor(Color.Green)
                    .WithDescription($"Derzeit sind {onlineUsers.Count} Benutzer online.")
                    .WithCurrentTimestamp();
                
                foreach (var user in onlineUsers)
                {
                    embed.AddField(
                        user.Username,
                        $"Lizenz: `{user.LicenseKey}`\n" +
                        $"Gültig bis: {user.LicenseExpiration:dd.MM.yyyy}\n" +
                        $"IP: {user.IP}\n" +
                        $"Verbunden seit: {user.LastLogin:dd.MM.yyyy HH:mm}\n" +
                        $"Client-ID: `{user.ClientId}`"
                    );
                }
                
                await RespondAsync(embed: embed.Build(), ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin online' Commands: {ex.Message}");
                await RespondAsync("Beim Abrufen der Online-Benutzer ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
        
        [SlashCommand("broadcast", "Sendet eine Nachricht an alle verbundenen Clients")]
        public async Task BroadcastCommand(string message)
        {
            logger.Info($"Command 'admin broadcast' ausgeführt von {Context.User.Username}: {message}");
            
            try
            {
                serverHandler.BroadcastMessage(message);
                
                await RespondAsync($"Nachricht wurde an alle verbundenen Clients gesendet: \"{message}\"", ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin broadcast' Commands: {ex.Message}");
                await RespondAsync("Beim Senden der Broadcast-Nachricht ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
        
        [SlashCommand("kick", "Trennt einen Benutzer vom Server")]
        public async Task KickCommand(string username, string reason = "Von einem Administrator getrennt")
        {
            logger.Info($"Command 'admin kick' ausgeführt von {Context.User.Username}: {username}, Grund: {reason}");
            
            try
            {
                var users = licenseServer.GetAllUsers();
                var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                
                if (user == null)
                {
                    await RespondAsync($"Benutzer '{username}' nicht gefunden.", ephemeral: true);
                    return;
                }
                
                if (!user.IsOnline)
                {
                    await RespondAsync($"Benutzer '{username}' ist nicht online.", ephemeral: true);
                    return;
                }
                
                serverHandler.DisconnectClient(user.ClientId, reason);
                
                await RespondAsync($"Benutzer '{username}' wurde vom Server getrennt. Grund: {reason}", ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin kick' Commands: {ex.Message}");
                await RespondAsync("Beim Trennen des Benutzers ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
        
        [SlashCommand("extend", "Verlängert eine Lizenz")]
        public async Task ExtendCommand(string licenseKey, int days)
        {
            logger.Info($"Command 'admin extend' ausgeführt von {Context.User.Username}: {licenseKey}, {days} Tage");
            
            try
            {
                var result = serverHandler.ExtendLicense(licenseKey, days);
                
                if (result)
                {
                    var users = licenseServer.GetAllUsers();
                    var user = users.FirstOrDefault(u => u.LicenseKey == licenseKey);
                    
                    if (user != null)
                    {
                        await RespondAsync(
                            $"Die Lizenz für Benutzer '{user.Username}' wurde um {days} Tage verlängert.\n" +
                            $"Neues Ablaufdatum: {user.LicenseExpiration:dd.MM.yyyy}", 
                            ephemeral: true);
                    }
                    else
                    {
                        await RespondAsync($"Die Lizenz '{licenseKey}' wurde um {days} Tage verlängert.", ephemeral: true);
                    }
                }
                else
                {
                    await RespondAsync($"Die Lizenz '{licenseKey}' konnte nicht verlängert werden.", ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin extend' Commands: {ex.Message}");
                await RespondAsync("Beim Verlängern der Lizenz ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
        
        [SlashCommand("message", "Sendet eine Nachricht an einen bestimmten Client")]
        public async Task MessageCommand(string username, string message)
        {
            logger.Info($"Command 'admin message' ausgeführt von {Context.User.Username}: {username}, {message}");
            
            try
            {
                var users = licenseServer.GetAllUsers();
                var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                
                if (user == null)
                {
                    await RespondAsync($"Benutzer '{username}' nicht gefunden.", ephemeral: true);
                    return;
                }
                
                if (!user.IsOnline)
                {
                    await RespondAsync($"Benutzer '{username}' ist nicht online.", ephemeral: true);
                    return;
                }
                
                serverHandler.SendMessageToClient(user.ClientId, message);
                
                await RespondAsync($"Nachricht wurde an Benutzer '{username}' gesendet: \"{message}\"", ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.Error($"Fehler beim Ausführen des 'admin message' Commands: {ex.Message}");
                await RespondAsync("Beim Senden der Nachricht ist ein Fehler aufgetreten.", ephemeral: true);
            }
        }
    }
}