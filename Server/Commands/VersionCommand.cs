using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Server.Game;
using Server.Utils;

namespace Server.Commands
{
    public class VersionCommand : Console.ConsoleCommand
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ulong _forumId;

        public VersionCommand(
            DiscordSocketClient client,
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService) 
            : base(
                "version", 
                "Aktualisiert die Version eines hochgeladenen Spiels", 
                "<spiel_name_oder_id> <neue_version>", 
                "version \"Grand Theft Auto V\" 1.0.1", 
                null)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _gameInfoService = gameInfoService;
            
            _forumId = _config.GetValue<ulong>("Forum:GamesForumId", 0);
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 2)
            {
                WriteError("Du musst einen Spielnamen oder eine ID und eine neue Version angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string gameNameOrId = args[0];
            string newVersion = args[1];

            try
            {
                // 1. Hole Spielinformationen
                var gameInfo = await _gameInfoService.GetGameInfoAsync(gameNameOrId);
                if (gameInfo == null)
                {
                    WriteError($"Konnte keine Informationen für Spiel '{gameNameOrId}' finden.");
                    return;
                }
                
                WriteInfo($"Spiel gefunden: {gameInfo.Title} (IGDB-ID: {gameInfo.Id})");
                
                // 2. Finde den Discord-Kanal
                if (_forumId == 0)
                {
                    WriteError("Keine Forum-ID in der Konfiguration angegeben.");
                    return;
                }
                
                var channel = _client.GetChannel(_forumId);
                if (channel == null)
                {
                    WriteError($"Channel mit ID {_forumId} nicht gefunden.");
                    return;
                }
                
                IEnumerable<IMessage> messages;
                
                // 3. Je nach Kanaltyp unterschiedlich vorgehen
                if (channel is SocketForumChannel forumChannel)
                {
                    // Es ist ein Forum - suche nach einem Thread für das Spiel
                    var threads = await forumChannel.GetActiveThreadsAsync();
                    var gameThread = threads.FirstOrDefault(t => 
                        t.Name.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase));
                    
                    if (gameThread == null)
                    {
                        WriteError($"Kein Thread für Spiel '{gameInfo.Title}' gefunden.");
                        return;
                    }
                    
                    WriteInfo($"Thread für {gameInfo.Title} gefunden (ID: {gameThread.Id}).");
                    
                    var messageChannel = gameThread as IMessageChannel;
                    if (messageChannel == null)
                    {
                        WriteError("Konnte Thread nicht als MessageChannel abrufen.");
                        return;
                    }
                    
                    messages = await messageChannel.GetMessagesAsync(100).FlattenAsync();
                }
                else if (channel is IMessageChannel textChannel)
                {
                    // Es ist ein normaler Text-Channel
                    var allMessages = await textChannel.GetMessagesAsync(200).FlattenAsync();
                    // Filtere nach Nachrichten, die den Spieltitel enthalten
                    messages = allMessages.Where(m => 
                        m.Content?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true || 
                        m.Embeds.Any(e => e.Title?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true));
                }
                else
                {
                    WriteError($"Channel mit ID {_forumId} ist weder ein Forum noch ein Text-Channel.");
                    return;
                }
                
                // 4. Finde das Version-Embed
                var versionMessage = messages
                    .Where(m => m.Author.Id == _client.CurrentUser.Id && m.Embeds.Any())
                    .FirstOrDefault(m => m.Embeds.Any(e => 
                        e.Title?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true && 
                        e.Title?.Contains("Version", StringComparison.OrdinalIgnoreCase) == true));
                
                if (versionMessage == null)
                {
                    WriteError("Konnte kein Version-Embed für dieses Spiel finden.");
                    return;
                }
                
                // 5. Hole das aktuelle Embed und aktualisiere es
                var oldEmbed = versionMessage.Embeds.First();
                
                // Erstelle ein neues Embed mit aktualisierten Informationen
                var embedBuilder = new EmbedBuilder()
                    .WithTitle(oldEmbed.Title?.Replace(GetVersionFromTitle(oldEmbed.Title), newVersion))
                    .WithDescription(oldEmbed.Description)
                    .WithColor(oldEmbed.Color ?? Color.Green)
                    .WithFooter(oldEmbed.Footer?.Text)
                    .WithCurrentTimestamp();
                
                // Kopiere alle Felder außer dem Versionsfeld
                foreach (var field in oldEmbed.Fields)
                {
                    if (field.Name == "Version")
                    {
                        // Aktualisiere das Versionsfeld
                        embedBuilder.AddField("Version", newVersion, field.Inline);
                    }
                    else
                    {
                        embedBuilder.AddField(field.Name, field.Value, field.Inline);
                    }
                }
                
                // 6. Aktualisiere das Embed
                try
                {
                    await (versionMessage as IUserMessage).ModifyAsync(m => m.Embed = embedBuilder.Build());
                    
                    string oldVersion = oldEmbed.Fields.FirstOrDefault(f => f.Name == "Version").Value ?? "unbekannt";
                    WriteSuccess($"Version für {gameInfo.Title} wurde von {oldVersion} auf {newVersion} aktualisiert.");
                }
                catch (Exception ex)
                {
                    WriteError($"Fehler beim Aktualisieren des Embeds: {ex.Message}");
                    
                    // Fallback: Sende eine neue Nachricht mit dem aktualisierten Embed
                    if (versionMessage.Channel is IMessageChannel messageChannel)
                    {
                        await messageChannel.SendMessageAsync(
                            text: $"**Download-Informationen für {gameInfo.Title} (Aktualisierte Version)**",
                            embed: embedBuilder.Build());
                        
                        WriteInfo("Eine neue Nachricht mit der aktualisierten Version wurde stattdessen gesendet.");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Aktualisieren der Version: {ex.Message}");
                _logger.Error($"Fehler beim Version-Command: {ex}");
            }
        }
        
        // Hilfsmethode zum Extrahieren der Version aus einem Embed-Titel
        private string GetVersionFromTitle(string title)
        {
            try
            {
                // Format: Spiel - Version X.Y.Z
                int versionIndex = title.LastIndexOf("Version", StringComparison.OrdinalIgnoreCase);
                if (versionIndex != -1)
                {
                    return title.Substring(versionIndex + "Version".Length).Trim();
                }
                return ""; // Leerer String, wenn keine Version gefunden wurde
            }
            catch
            {
                return ""; // Im Fehlerfall leeren String zurückgeben
            }
        }
    }
}