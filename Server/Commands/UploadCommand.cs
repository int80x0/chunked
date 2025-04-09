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
    public class UploadCommand : Console.ConsoleCommand
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ChunkManager _chunkManager;

        private readonly ulong _forumId;
        
        public UploadCommand(
            DiscordSocketClient client,
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService,
            ChunkManager chunkManager) 
            : base(
                "upload", 
                "Lädt eine Spieldatei in ein Discord-Forum hoch", 
                "<pfad_zur_datei> [version]", 
                "upload C:\\Games\\GTA5\\20952.zip 1.0.0", 
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
                
                return new List<string> { "*.zip", "*.rar", "*.7z", "*.iso", "*.mkv", "*.mp4" };
            }
            
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
            else if (File.Exists(current))
            {
                
                return new List<string> { current + " 1.0.0", current + " 1.0.1", current + " latest" };
            }
            else
            {
                
                try
                {
                    string directory = Path.GetDirectoryName(current) ?? "";
                    string filename = Path.GetFileName(current);
                    
                    if (Directory.Exists(directory))
                    {
                        return Directory.GetFileSystemEntries(directory, filename + "*")
                            .ToList();
                    }
                }
                catch
                {
                    
                }
                
                return new List<string>();
            }
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                WriteError("Du musst einen Dateipfad angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string filePath = args[0];
            string version = args.Length > 1 ? args[1] : "1.0.0";
            
            if (!File.Exists(filePath))
            {
                WriteError($"Die Datei '{filePath}' existiert nicht.");
                return;
            }

            WriteInfo($"Verarbeite Datei: {filePath}");
            
            try
            {
                
                string gameId = Path.GetFileNameWithoutExtension(filePath);
                
                
                var gameInfo = await _gameInfoService.GetGameInfoAsync(gameId);
                if (gameInfo == null)
                {
                    WriteError($"Konnte keine Informationen für Spiel mit ID {gameId} finden.");
                    return;
                }
                
                WriteSuccess($"Spiel erkannt: {gameInfo.Title}");
                
                
                WriteInfo("Teile Datei in Chunks...");
                var fileInfo = new FileInfo(filePath);
                var chunks = await _chunkManager.CreateChunksAsync(filePath);
                
                WriteSuccess($"Datei in {chunks.Count} Chunks aufgeteilt.");
                
                
                if (_forumId == 0)
                {
                    WriteError("Keine Forum-ID in der Konfiguration angegeben.");
                    return;
                }
                
                
                var gameEmbed = new EmbedBuilder()
                    .WithTitle(gameInfo.Title)
                    .WithDescription(gameInfo.Description ?? "Keine Beschreibung verfügbar.")
                    .WithColor(Color.Blue)
                    .WithThumbnailUrl(gameInfo.ThumbnailUrl)
                    .WithImageUrl(gameInfo.CoverUrl)
                    .WithFields(
                        new EmbedFieldBuilder().WithName("Genre").WithValue(gameInfo.Genre ?? "Unbekannt").WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Entwickler").WithValue(gameInfo.Developer ?? "Unbekannt").WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Erscheinungsdatum").WithValue(gameInfo.ReleaseDate?.ToString("dd.MM.yyyy") ?? "Unbekannt").WithIsInline(true)
                    )
                    .WithFooter($"TGDB-ID: {gameId}")
                    .WithCurrentTimestamp()
                    .Build();
                
                
                string fileId = Guid.NewGuid().ToString("N");
                var versionEmbed = new EmbedBuilder()
                    .WithTitle($"{gameInfo.Title} - Version {version}")
                    .WithDescription("Spiel-Download-Informationen")
                    .WithColor(Color.Green)
                    .WithFields(
                        new EmbedFieldBuilder().WithName("Version").WithValue(version).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Dateigröße").WithValue(_chunkManager.FormatFileSize(fileInfo.Length)).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Hochgeladen am").WithValue(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Chunks").WithValue(chunks.Count).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Datei-ID").WithValue(fileId).WithIsInline(false),
                        new EmbedFieldBuilder().WithName("Download-Befehl").WithValue($"```\n/download {gameId}\n```").WithIsInline(false)
                    )
                    .WithFooter("Zum Herunterladen wird ein Client mit gültiger Lizenz benötigt.")
                    .WithCurrentTimestamp()
                    .Build();
                
                
                var channel = _client.GetChannel(_forumId);
                if (channel == null)
                {
                    WriteError($"Channel mit ID {_forumId} nicht gefunden.");
                    return;
                }
                
                
                if (channel is SocketForumChannel forumChannel)
                {
                    
                    await ProcessInForumChannel(forumChannel, gameInfo, gameId, gameEmbed, versionEmbed, chunks, version, fileId);
                    
                }
                else if (channel is ITextChannel textChannel)
                {
                    
                    await ProcessInTextChannel(textChannel, gameInfo, gameId, gameEmbed, versionEmbed, chunks, version, fileId);
                }
                else
                {
                    WriteError($"Channel mit ID {_forumId} ist weder ein Forum noch ein Text-Channel.");
                    return;
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Upload: {ex.Message}");
                _logger.Error($"Fehler beim Upload-Command: {ex}");
            }
        }
        
        private async Task ProcessInForumChannel(SocketForumChannel forumChannel, GameInfo gameInfo, string gameId, 
            Embed gameEmbed, Embed versionEmbed, List<FileChunk> chunks, string version, string fileId)
        {
            try
            {
                WriteInfo($"Forum erkannt, erstelle einen Thread für {gameInfo.Title}...");
                
                
                var threads = await forumChannel.GetActiveThreadsAsync();
                var existingThread = threads.FirstOrDefault(t => 
                    t.Name.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase));
                
                IMessageChannel threadChannel;
                
                if (existingThread != null)
                {
                    WriteInfo($"Existierender Thread für {gameInfo.Title} gefunden.");
                    threadChannel = existingThread as IMessageChannel;
                }
                else
                {
                    
                    
                    var applicableTagIds = new List<ulong>();
                    
                    try
                    {
                        
                        if (!string.IsNullOrEmpty(gameInfo.Genre))
                        {
                            var genreTag = forumChannel.Tags.FirstOrDefault(t => 
                                t.Name.Contains(gameInfo.Genre, StringComparison.OrdinalIgnoreCase));
                            
                            if (genreTag != null)
                            {
                                applicableTagIds.Add(genreTag.Id);
                            }
                        }
                        
                        
                        if (applicableTagIds.Count == 0)
                        {
                            var gameTag = forumChannel.Tags.FirstOrDefault(t => 
                                t.Name.Contains("Game", StringComparison.OrdinalIgnoreCase) || 
                                t.Name.Contains("Spiel", StringComparison.OrdinalIgnoreCase));
                            
                            if (gameTag != null)
                            {
                                applicableTagIds.Add(gameTag.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteWarning($"Fehler beim Abrufen der Tags: {ex.Message}. Fahre ohne Tags fort.");
                        applicableTagIds.Clear();
                    }
                    
                    try
                    {
                        
                        
                        
                        
                        try
                        {
                            var methodInfo = forumChannel.GetType().GetMethod("CreatePostAsync");
                            if (methodInfo != null)
                            {
                                
                                var thread = await forumChannel.CreatePostAsync(
                                    $"**{gameInfo.Title}** (ID: {gameId})",
                                    embeds: [gameEmbed]);
                                
                                threadChannel = thread as IMessageChannel;
                                WriteSuccess($"Forum-Thread für {gameInfo.Title} mit CreatePostAsync erstellt.");
                                
                                await threadChannel.SendMessageAsync(
                                    text: $"**Download-Informationen für {gameInfo.Title}**",
                                    embed: versionEmbed);
                
                                
                                await UploadChunksAsync(threadChannel, chunks, gameInfo.Title, fileId);
                
                                WriteSuccess($"Upload für {gameInfo.Title} v{version} abgeschlossen!");
                            }
                            else
                            {
                                throw new InvalidOperationException("CreatePostAsync nicht verfügbar");
                            }
                        }
                        catch (Exception)
                        {
                            
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteError($"Fehler beim Erstellen des Forum-Threads: {ex.Message}");
                        return;
                    }
                }
                
            }
            catch (Exception ex)
            {
                WriteError($"Fehler im Forum-Channel: {ex.Message}");
                _logger.Error($"Fehler beim ProcessInForumChannel: {ex}");
                throw;
            }
        }

        private async Task ProcessInTextChannel(ITextChannel channel, GameInfo gameInfo, string gameId, 
            Embed gameEmbed, Embed versionEmbed, List<FileChunk> chunks, string version, string fileId)
        {
            try
            {
                
                var mainMessage = await channel.SendMessageAsync(
                    text: $"**{gameInfo.Title}** (ID: {gameId})", 
                    embed: gameEmbed);
                
                await channel.SendMessageAsync(
                    text: $"**Download-Informationen für {gameInfo.Title}**",
                    embed: versionEmbed);
                
                
                await UploadChunksAsync(channel, chunks, gameInfo.Title, fileId);
                
                WriteSuccess($"Upload für {gameInfo.Title} v{version} abgeschlossen!");
            }
            catch (Exception ex)
            {
                WriteError($"Fehler im Text-Channel: {ex.Message}");
                _logger.Error($"Fehler beim ProcessInTextChannel: {ex}");
                throw;
            }
        }

        private async Task UploadChunksAsync(IMessageChannel channel, List<FileChunk> chunks, string gameTitle, string fileId)
        {
            try
            {
                WriteInfo($"Lade {chunks.Count} Chunks für {gameTitle} hoch...");
                
                
                await channel.SendMessageAsync($"**Chunks für {gameTitle}** (Datei-ID: {fileId})");
                
                
                var reassembleEmbed = new EmbedBuilder()
                    .WithTitle($"Reassemblierungs-Informationen für {gameTitle}")
                    .WithDescription("Bitte Chunks in der richtigen Reihenfolge zusammenfügen.")
                    .WithColor(Color.Orange)
                    .WithFields(
                        new EmbedFieldBuilder().WithName("Anzahl Chunks").WithValue(chunks.Count.ToString()).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Datei-ID").WithValue(fileId).WithIsInline(true),
                        new EmbedFieldBuilder().WithName("Hinweis").WithValue("Verwende den Client-Befehl /download, um diese Datei automatisch herunterzuladen.").WithIsInline(false)
                    )
                    .WithFooter("Chunks werden in den folgenden Nachrichten hochgeladen.")
                    .WithCurrentTimestamp()
                    .Build();
                
                await channel.SendMessageAsync(embed: reassembleEmbed);
                
                
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
                            .WithDescription($"Game: {gameTitle}")
                            .WithColor(Color.Orange)
                            .WithFields(
                                new EmbedFieldBuilder().WithName("Größe").WithValue(_chunkManager.FormatFileSize(chunk.Size)).WithIsInline(true),
                                new EmbedFieldBuilder().WithName("Chunk-ID").WithValue(chunk.Id).WithIsInline(true),
                                new EmbedFieldBuilder().WithName("Hash").WithValue(chunk.Hash).WithIsInline(true)
                            )
                            .WithFooter($"Datei-ID: {fileId} | Chunk {chunk.Index + 1} von {chunks.Count}")
                            .WithCurrentTimestamp()
                            .Build();
                        
                        
                        await channel.SendFileAsync(
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
                
                
                await channel.SendMessageAsync($"✅ **Alle {chunks.Count} Chunks für {gameTitle} wurden erfolgreich hochgeladen.**");
                
                WriteSuccess($"Alle Chunks für {gameTitle} wurden erfolgreich hochgeladen.");
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Hochladen der Chunks: {ex.Message}");
                _logger.Error($"Fehler beim UploadChunksAsync: {ex}");
                throw;
            }
        }
    }
}