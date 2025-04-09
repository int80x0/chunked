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
                
                var gameInfo = await _gameInfoService.GetGameInfoAsync(gameNameOrId);
                if (gameInfo == null)
                {
                    WriteError($"Konnte keine Informationen für Spiel '{gameNameOrId}' finden.");
                    return;
                }
                
                WriteInfo($"Spiel gefunden: {gameInfo.Title} (IGDB-ID: {gameInfo.Id})");
                
                
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
                
                
                if (channel is SocketForumChannel forumChannel)
                {
                    
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
                    
                    var allMessages = await textChannel.GetMessagesAsync(200).FlattenAsync();
                    
                    messages = allMessages.Where(m => 
                        m.Content?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true || 
                        m.Embeds.Any(e => e.Title?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true));
                }
                else
                {
                    WriteError($"Channel mit ID {_forumId} ist weder ein Forum noch ein Text-Channel.");
                    return;
                }
                
                
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
                
                
                var oldEmbed = versionMessage.Embeds.First();
                
                
                var embedBuilder = new EmbedBuilder()
                    .WithTitle(oldEmbed.Title?.Replace(GetVersionFromTitle(oldEmbed.Title), newVersion))
                    .WithDescription(oldEmbed.Description)
                    .WithColor(oldEmbed.Color ?? Color.Green)
                    .WithFooter(oldEmbed.Footer?.Text)
                    .WithCurrentTimestamp();
                
                
                foreach (var field in oldEmbed.Fields)
                {
                    if (field.Name == "Version")
                    {
                        
                        embedBuilder.AddField("Version", newVersion, field.Inline);
                    }
                    else
                    {
                        embedBuilder.AddField(field.Name, field.Value, field.Inline);
                    }
                }
                
                
                try
                {
                    await (versionMessage as IUserMessage).ModifyAsync(m => m.Embed = embedBuilder.Build());
                    
                    string oldVersion = oldEmbed.Fields.FirstOrDefault(f => f.Name == "Version").Value ?? "unbekannt";
                    WriteSuccess($"Version für {gameInfo.Title} wurde von {oldVersion} auf {newVersion} aktualisiert.");
                }
                catch (Exception ex)
                {
                    WriteError($"Fehler beim Aktualisieren des Embeds: {ex.Message}");
                    
                    
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
        
        
        private string GetVersionFromTitle(string title)
        {
            try
            {
                
                int versionIndex = title.LastIndexOf("Version", StringComparison.OrdinalIgnoreCase);
                if (versionIndex != -1)
                {
                    return title.Substring(versionIndex + "Version".Length).Trim();
                }
                return ""; 
            }
            catch
            {
                return ""; 
            }
        }
    }
}