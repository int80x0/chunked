using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Server.Game;
using Server.Utils;

namespace Server.Commands
{
    public class UpdateCommand : Console.ConsoleCommand
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ChunkManager _chunkManager;

        private readonly ulong _forumId;
        
        public UpdateCommand(
            DiscordSocketClient client,
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService,
            ChunkManager chunkManager) 
            : base(
                "update", 
                "Aktualisiert ein hochgeladenes Spiel mit einer neuen Datei und Version", 
                "<spiel_name_oder_id> <pfad_zur_datei> <neue_version>", 
                "update \"Grand Theft Auto V\" C:\\Games\\GTAV-Update.zip 1.0.1", 
                GetAutocompleteOptionsAsync)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _gameInfoService = gameInfoService;
            _chunkManager = chunkManager;
            
            _forumId = _config.GetValue<ulong>("Forum:GamesForumId", 0);
        }

        private static async Task<List<string>> GetAutocompleteOptionsAsync(string[] args)
        {
            if (args.Length == 0)
            {
                
                return new List<string> { "Grand Theft Auto V", "The Witcher 3", "Fallout 4", "Cyberpunk 2077" };
            }
            else if (args.Length == 1)
            {
                
                string current = args[0];
                
                if (Directory.Exists(current))
                {
                    
                    try
                    {
                        var result = new List<string>();
                        
                        var directories = Directory.GetDirectories(current)
                            .Select(d => d + Path.DirectorySeparatorChar);
                        
                        var files = Directory.GetFiles(current)
                            .Where(f => Path.GetExtension(f).ToLower() is ".zip" or ".rar" or ".7z" or ".iso" or ".mkv" or ".mp4");
                        
                        result.AddRange(directories);
                        result.AddRange(files);
                        
                        return result;
                    }
                    catch
                    {
                        return new List<string>();
                    }
                }
            }
            else if (args.Length == 2)
            {
                
                return new List<string> { args[0] + " " + args[1] + " 1.0.1", args[0] + " " + args[1] + " 1.1.0", args[0] + " " + args[1] + " 2.0.0" };
            }
            
            return new List<string>();
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 3)
            {
                WriteError("Du musst einen Spielnamen, einen Dateipfad und eine neue Version angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string gameNameOrId = args[0];
            string filePath = args[1];
            string newVersion = args[2];
            
            if (!File.Exists(filePath))
            {
                WriteError($"Die Datei '{filePath}' existiert nicht.");
                return;
            }

            WriteInfo($"Starte Update für Spiel: {gameNameOrId} mit Datei: {filePath}, neue Version: {newVersion}");
            
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
                
                IMessageChannel messageChannel;
                IEnumerable<IMessage> messages;
                
                
                if (channel is SocketForumChannel forumChannel)
                {
                    
                    var threads = await forumChannel.GetActiveThreadsAsync();
                    var gameThread = threads.FirstOrDefault(t => 
                        t.Name.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase));
                    
                    if (gameThread == null)
                    {
                        WriteError($"Kein Thread für Spiel '{gameInfo.Title}' gefunden. Verwende den 'upload' Befehl zuerst.");
                        return;
                    }
                    
                    WriteInfo($"Thread für {gameInfo.Title} gefunden (ID: {gameThread.Id}).");
                    
                    messageChannel = gameThread as IMessageChannel;
                    if (messageChannel == null)
                    {
                        WriteError("Konnte Thread nicht als MessageChannel abrufen.");
                        return;
                    }
                    
                    messages = await messageChannel.GetMessagesAsync(200).FlattenAsync();
                }
                else if (channel is IMessageChannel textChannel)
                {
                    
                    messageChannel = textChannel;
                    
                    
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
                    WriteError("Konnte kein Version-Embed für dieses Spiel finden. Verwende den 'upload' Befehl zuerst.");
                    return;
                }
                
                
                string oldFileId = null;
                foreach (var embed in versionMessage.Embeds)
                {
                    var fileIdField = embed.Fields.FirstOrDefault(f => 
                        f.Name.Contains("Datei-ID", StringComparison.OrdinalIgnoreCase));
                    
                    if (fileIdField != null && !string.IsNullOrEmpty(fileIdField.Value))
                    {
                        oldFileId = fileIdField.Value;
                        break;
                    }
                }
                
                if (oldFileId == null)
                {
                    WriteError("Konnte keine Datei-ID im Version-Embed finden.");
                    return;
                }
                
                WriteInfo($"Alte Datei-ID gefunden: {oldFileId}");
                
                
                var chunkMessages = messages
                    .Where(m => m.Embeds.Any(e => e.Title?.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase) == true &&
                                          e.Footer?.Text?.Contains(oldFileId) == true))
                    .ToList();
                
                WriteInfo($"Gefunden: {chunkMessages.Count} alte Chunks");
                
                
                var chunkAnnouncementMessages = messages
                    .Where(m => m.Content?.Contains($"**Chunks für {gameInfo.Title}**", StringComparison.OrdinalIgnoreCase) == true ||
                               m.Embeds.Any(e => e.Title?.Contains("Reassemblierungs-Informationen", StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();
                
                
                var completionMessage = messages
                    .FirstOrDefault(m => m.Content?.Contains($"✅ **Alle", StringComparison.OrdinalIgnoreCase) == true &&
                                     m.Content?.Contains($"Chunks für {gameInfo.Title}", StringComparison.OrdinalIgnoreCase) == true);
                
                
                WriteWarning($"Bereit, {chunkMessages.Count} Chunks zu löschen und die Version von {gameInfo.Title} zu aktualisieren.");
                if (!await ConfirmActionAsync($"Möchtest du fortfahren und die Version auf {newVersion} aktualisieren? (j/n)"))
                {
                    WriteInfo("Update abgebrochen.");
                    return;
                }
                
                
                WriteInfo("Lösche alte Chunk-Nachrichten...");
                int deletedCount = 0;
                
                foreach (var msg in chunkMessages.Concat(chunkAnnouncementMessages))
                {
                    try
                    {
                        if (msg is IUserMessage userMsg)
                        {
                            await userMsg.DeleteAsync();
                            deletedCount++;
                            
                            
                            if (deletedCount % 5 == 0)
                            {
                                await Task.Delay(1000);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Fehler beim Löschen einer Nachricht: {ex.Message}");
                    }
                }
                
                
                if (completionMessage != null && completionMessage is IUserMessage completionUserMsg)
                {
                    try
                    {
                        await completionUserMsg.DeleteAsync();
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Fehler beim Löschen der Abschluss-Nachricht: {ex.Message}");
                    }
                }
                
                WriteSuccess($"{deletedCount} alte Nachrichten gelöscht.");
                
                
                WriteInfo("Aktualisiere Version-Embed...");
                
                
                var oldEmbed = versionMessage.Embeds.First();
                
                
                string newFileId = Guid.NewGuid().ToString("N");
                
                
                WriteInfo("Teile neue Datei in Chunks...");
                var fileInfo = new FileInfo(filePath);
                var chunks = await _chunkManager.CreateChunksAsync(filePath);
                
                WriteSuccess($"Neue Datei in {chunks.Count} Chunks aufgeteilt.");
                
                
                var embedBuilder = new EmbedBuilder()
                    .WithTitle($"{gameInfo.Title} - Version {newVersion}")
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
                    else if (field.Name == "Dateigröße")
                    {
                        embedBuilder.AddField("Dateigröße", _chunkManager.FormatFileSize(fileInfo.Length), field.Inline);
                    }
                    else if (field.Name == "Hochgeladen am")
                    {
                        embedBuilder.AddField("Hochgeladen am", DateTime.Now.ToString("dd.MM.yyyy HH:mm"), field.Inline);
                    }
                    else if (field.Name == "Chunks")
                    {
                        embedBuilder.AddField("Chunks", chunks.Count.ToString(), field.Inline);
                    }
                    else if (field.Name == "Datei-ID")
                    {
                        embedBuilder.AddField("Datei-ID", newFileId, field.Inline);
                    }
                    else
                    {
                        
                        embedBuilder.AddField(field.Name, field.Value, field.Inline);
                    }
                }
                
                
                try
                {
                    await (versionMessage as IUserMessage).ModifyAsync(m => m.Embed = embedBuilder.Build());
                    WriteSuccess("Version-Embed aktualisiert.");
                }
                catch (Exception ex)
                {
                    WriteError($"Fehler beim Aktualisieren des Version-Embeds: {ex.Message}");
                    return;
                }
                
                
                try
                {
                    WriteInfo($"Lade {chunks.Count} neue Chunks für {gameInfo.Title} hoch...");
                    
                    
                    await messageChannel.SendMessageAsync($"**Chunks für {gameInfo.Title}** (Datei-ID: {newFileId})");
                    
                    
                    var reassembleEmbed = new EmbedBuilder()
                        .WithTitle($"Reassemblierungs-Informationen für {gameInfo.Title}")
                        .WithDescription("Bitte Chunks in der richtigen Reihenfolge zusammenfügen.")
                        .WithColor(Color.Orange)
                        .WithFields(
                            new EmbedFieldBuilder().WithName("Anzahl Chunks").WithValue(chunks.Count.ToString()).WithIsInline(true),
                            new EmbedFieldBuilder().WithName("Datei-ID").WithValue(newFileId).WithIsInline(true),
                            new EmbedFieldBuilder().WithName("Hinweis").WithValue("Verwende den Client-Befehl /download, um diese Datei automatisch herunterzuladen.").WithIsInline(false)
                        )
                        .WithFooter("Chunks werden in den folgenden Nachrichten hochgeladen.")
                        .WithCurrentTimestamp()
                        .Build();
                    
                    await messageChannel.SendMessageAsync(embed: reassembleEmbed);
                    
                    
                    const int batchSize = 10;
                    int totalBatches = (int)Math.Ceiling(chunks.Count / (double)batchSize);
                    
                    for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                    {
                        var batchChunks = chunks
                            .Skip(batchIndex * batchSize)
                            .Take(batchSize)
                            .ToList();
                        
                        WriteInfo($"Verarbeite Batch {batchIndex + 1}/{totalBatches} ({batchChunks.Count} Chunks)...");
                        
                        foreach (var chunk in batchChunks)
                        {
                            WriteInfo($"Lade Chunk {chunk.Index + 1}/{chunks.Count} hoch...");
                            
                            using var fileStream = new FileStream(chunk.FilePath, FileMode.Open, FileAccess.Read);
                            
                            
                            var embed = new EmbedBuilder()
                                .WithTitle($"Chunk {chunk.Index + 1}/{chunks.Count}")
                                .WithDescription($"Game: {gameInfo.Title}")
                                .WithColor(Color.Orange)
                                .WithFields(
                                    new EmbedFieldBuilder().WithName("Größe").WithValue(_chunkManager.FormatFileSize(chunk.Size)).WithIsInline(true),
                                    new EmbedFieldBuilder().WithName("Chunk-ID").WithValue(chunk.Id).WithIsInline(true),
                                    new EmbedFieldBuilder().WithName("Hash").WithValue(chunk.Hash).WithIsInline(true)
                                )
                                .WithFooter($"Datei-ID: {newFileId} | Chunk {chunk.Index + 1} von {chunks.Count}")
                                .WithCurrentTimestamp()
                                .Build();
                            
                            
                            await messageChannel.SendFileAsync(
                                fileStream, 
                                Path.GetFileName(chunk.FilePath), 
                                embed: embed);
                            
                            WriteInfo($"Chunk {chunk.Index + 1}/{chunks.Count} hochgeladen");
                            
                            
                            await Task.Delay(1000);
                        }
                        
                        
                        if (batchIndex < totalBatches - 1)
                        {
                            WriteInfo($"Warte vor dem nächsten Batch...");
                            await Task.Delay(5000);
                        }
                    }
                    
                    
                    await messageChannel.SendMessageAsync($"✅ **Alle {chunks.Count} Chunks für {gameInfo.Title} wurden erfolgreich hochgeladen.**");
                    
                    WriteSuccess($"Alle Chunks für {gameInfo.Title} wurden erfolgreich hochgeladen.");
                    WriteSuccess($"Update für {gameInfo.Title} auf Version {newVersion} abgeschlossen!");
                }
                catch (Exception ex)
                {
                    WriteError($"Fehler beim Hochladen der neuen Chunks: {ex.Message}");
                    _logger.Error($"Fehler beim UploadChunksAsync: {ex}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Update: {ex.Message}");
                _logger.Error($"Fehler beim Update-Command: {ex}");
            }
        }
    }
}