using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LicenseSystem.Services;
using Microsoft.Extensions.Configuration;
using Server.Game;
using Server.Utils;

namespace Server.Commands
{
    public class DownloadCommand : Console.ConsoleCommand
    {
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly LicenseServer _licenseServer;
        private readonly GameInfoService _gameInfoService;
        private readonly DiscordSocketClient _client;
        private readonly HttpClient _httpClient;
        private readonly ulong _forumId;

        public DownloadCommand(
            IConfiguration config,
            Logger logger,
            LicenseServer licenseServer,
            GameInfoService gameInfoService,
            DiscordSocketClient client) 
            : base(
                "download", 
                "Bereitet den Download einer Spieldatei vor und sendet die Download-Informationen an den Client (oder lädt die Datei direkt herunter)", 
                "<spiel_name_oder_id> [client_id | \"local\" <ausgabepfad>]", 
                "download \"Grand Theft Auto V\" a1b2c3d4\ndownload \"Grand Theft Auto V\" local C:\\Downloads", 
                GetAutocompleteSuggestionsAsync)
        {
            _config = config;
            _logger = logger;
            _licenseServer = licenseServer;
            _gameInfoService = gameInfoService;
            _client = client;
            _httpClient = new HttpClient();
            
            _forumId = _config.GetValue<ulong>("Forum:GamesForumId", 0);
        }

        private static async Task<List<string>> GetAutocompleteSuggestionsAsync(string[] args)
        {
            // Hier könnten wir eine Liste der verfügbaren Spiel-Namen zurückgeben
            // oder die Liste der Client-IDs für den zweiten Parameter
            if (args.Length == 0)
            {
                // Beispiele für Spiele
                return new List<string> { "Grand Theft Auto V", "The Witcher 3", "Fallout 4", "Cyberpunk 2077" };
            }
            else if (args.Length == 1)
            {
                // Optionen für den zweiten Parameter
                return new List<string> { args[0] + " local", args[0] + " client_id" };
            }
            else if (args.Length == 2 && args[1].ToLower() == "local")
            {
                // Bei "local" als Option, mögliche Pfade ergänzen
                string[] commonPaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    Directory.GetCurrentDirectory()
                };

                return commonPaths.Where(Directory.Exists).ToList();
            }
            
            return new List<string>();
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                WriteError("Du musst einen Spielnamen oder eine ID angeben.");
                System.Console.WriteLine($"Verwendung: {Name} {Usage}");
                return;
            }

            string gameNameOrId = args[0];
            bool downloadLocally = args.Length > 1 && args[1].ToLower() == "local";
            string outputPath = downloadLocally && args.Length > 2 ? args[2] : Directory.GetCurrentDirectory();
            string clientId = !downloadLocally && args.Length > 1 ? args[1] : null;

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
                
                // 2. Finde den Discord-Kanal für das Spiel
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
                
                IEnumerable<IMessage> allMessages;
                IMessageChannel messageChannel;
                
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
                    
                    messageChannel = gameThread as IMessageChannel;
                    if (messageChannel == null)
                    {
                        WriteError("Konnte Thread nicht als MessageChannel abrufen.");
                        return;
                    }
                    
                    allMessages = await messageChannel.GetMessagesAsync(200).FlattenAsync();
                }
                else if (channel is IMessageChannel textChannel)
                {
                    // Es ist ein normaler Text-Channel
                    messageChannel = textChannel;
                    
                    // Hole alle Nachrichten und filtere nach dem Spieltitel
                    var messages = await textChannel.GetMessagesAsync(200).FlattenAsync();
                    allMessages = messages.Where(m => 
                        m.Content?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true || 
                        m.Embeds.Any(e => e.Title?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true));
                }
                else
                {
                    WriteError($"Channel mit ID {_forumId} ist weder ein Forum noch ein Text-Channel.");
                    return;
                }
                
                // 4. Suche nach dem Version-Embed und der Datei-ID
                string fileId = null;
                
                foreach (var message in allMessages)
                {
                    foreach (var embed in message.Embeds)
                    {
                        var fileIdField = embed.Fields.FirstOrDefault(f => 
                            f.Name.Contains("Datei-ID", StringComparison.OrdinalIgnoreCase));
                        
                        if (fileIdField != null && !string.IsNullOrEmpty(fileIdField.Value))
                        {
                            fileId = fileIdField.Value;
                            break;
                        }
                    }
                    
                    if (fileId != null) break;
                }
                
                if (fileId == null)
                {
                    WriteError("Konnte keine Datei-ID finden.");
                    return;
                }
                
                WriteInfo($"Datei-ID gefunden: {fileId}");
                
                // 5. Sammle Chunks
                var chunkMessages = allMessages
                    .Where(m => m.Embeds.Any(e => e.Title?.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase) == true &&
                                              e.Footer?.Text?.Contains(fileId) == true))
                    .ToList();
                
                if (chunkMessages.Count == 0)
                {
                    WriteError("Keine Chunks für dieses Spiel gefunden.");
                    return;
                }
                
                WriteInfo($"{chunkMessages.Count} Chunks für {gameInfo.Title} gefunden.");
                
                // 6. Extrahiere Chunk-Informationen
                var chunks = new List<(int index, string id, long size, string url, string hash)>();
                
                foreach (var message in chunkMessages)
                {
                    if (message.Attachments.Count == 0) continue;
                    
                    foreach (var embed in message.Embeds)
                    {
                        if (embed.Title != null && embed.Title.StartsWith("Chunk"))
                        {
                            // Format: "Chunk X/Y"
                            string[] parts = embed.Title.Split(' ')[1].Split('/');
                            if (int.TryParse(parts[0], out int index))
                            {
                                string chunkId = null;
                                long size = 0;
                                string hash = null;
                                
                                // Extrahiere Chunk-ID, Größe und Hash aus den Embed-Feldern
                                foreach (var field in embed.Fields)
                                {
                                    if (field.Name == "Chunk-ID")
                                    {
                                        chunkId = field.Value;
                                    }
                                    else if (field.Name == "Größe")
                                    {
                                        size = ParseFileSize(field.Value);
                                    }
                                    else if (field.Name == "Hash")
                                    {
                                        hash = field.Value;
                                    }
                                }
                                
                                // Hol die Attachment-URL
                                var attachment = message.Attachments.First();
                                string url = attachment.Url;
                                
                                if (!string.IsNullOrEmpty(chunkId) && !string.IsNullOrEmpty(url))
                                {
                                    chunks.Add((index - 1, chunkId, size, url, hash));
                                }
                            }
                        }
                    }
                }
                
                chunks = chunks.OrderBy(c => c.index).ToList();
                
                if (chunks.Count == 0)
                {
                    WriteError("Konnte keine Chunks extrahieren.");
                    return;
                }
                
                WriteInfo($"Erfolgreich {chunks.Count} Chunks extrahiert.");
                
                // 7. Erstelle Download-Anweisung
                var downloadInfo = new
                {
                    GameId = gameInfo.Id,
                    GameTitle = gameInfo.Title,
                    FileId = fileId,
                    ChunkCount = chunks.Count,
                    Chunks = chunks.Select(c => new
                    {
                        Index = c.index,
                        Id = c.id,
                        Size = c.size,
                        Url = c.url,
                        Hash = c.hash
                    }).ToList(),
                    TotalSize = chunks.Sum(c => c.size)
                };
                
                string downloadJson = System.Text.Json.JsonSerializer.Serialize(downloadInfo, 
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                
                // 8. Je nach Modus unterschiedlich vorgehen
                if (downloadLocally)
                {
                    // Direkter Download auf den Server
                    await DownloadChunksLocallyAsync(gameInfo, downloadInfo, chunks, outputPath);
                }
                else if (!string.IsNullOrEmpty(clientId))
                {
                    // Sende direkt an Client
                    var clients = _licenseServer.GetAllUsers();
                    var client = clients.FirstOrDefault(c => c.ClientId == clientId);
                    
                    if (client == null)
                    {
                        WriteError($"Client mit ID {clientId} nicht gefunden oder nicht online.");
                        return;
                    }
                    
                    // Sende Download-Info an Client
                    _licenseServer.SendMessageToClient(clientId, downloadJson);
                    
                    WriteSuccess($"Download-Informationen für {gameInfo.Title} an Client {client.Username} gesendet.");
                }
                else
                {
                    // Speichere für späteren Abruf
                    string downloadsDir = _config.GetValue<string>("Game:DownloadsDirectory", "data/downloads");
                    if (!Directory.Exists(downloadsDir))
                    {
                        Directory.CreateDirectory(downloadsDir);
                    }
                    
                    string safeTitle = string.Join("_", gameInfo.Title.Split(Path.GetInvalidFileNameChars()));
                    string downloadFilePath = Path.Combine(downloadsDir, $"{safeTitle}_{fileId}.json");
                    await File.WriteAllTextAsync(downloadFilePath, downloadJson);
                    
                    WriteSuccess($"Download-Informationen für {gameInfo.Title} gespeichert: {downloadFilePath}");
                    WriteInfo($"Clients können diese Datei mit dem Befehl '/download \"{gameInfo.Title}\"' herunterladen.");
                }
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim Vorbereiten des Downloads: {ex.Message}");
                _logger.Error($"Fehler beim Download-Command: {ex}");
            }
        }

        private async Task DownloadChunksLocallyAsync(GameInfo gameInfo, dynamic downloadInfo, List<(int index, string id, long size, string url, string hash)> chunks, string outputDirectory)
        {
            try
            {
                // Ermittle die Dateierweiterung aus dem ersten Chunk
                string fileExtension = ".bin"; // Standardwert
                if (chunks.Count > 0 && !string.IsNullOrEmpty(chunks[0].url))
                {
                    // Versuche erst, die Erweiterung aus dem Attachment-Namen zu bekommen
                    string attachmentName = Path.GetFileName(new Uri(chunks[0].url).AbsolutePath);
                    
                    // Format ist typischerweise "name_index.extension.chunk"
                    if (attachmentName.Contains(".chunk"))
                    {
                        string nameWithoutChunk = attachmentName.Replace(".chunk", "");
                        int lastDotIndex = nameWithoutChunk.LastIndexOf('.');
                        if (lastDotIndex >= 0)
                        {
                            fileExtension = nameWithoutChunk.Substring(lastDotIndex);
                            WriteInfo($"Erkannte Dateierweiterung: {fileExtension}");
                        }
                    }
                }

                string safeTitle = string.Join("_", gameInfo.Title.Split(Path.GetInvalidFileNameChars()));
                string tempDir = Path.Combine(outputDirectory, $"temp_{safeTitle}_{Path.GetRandomFileName()}");
                string outputFilePath = Path.Combine(outputDirectory, $"{safeTitle}{fileExtension}");
                
                // Erstelle temporäres Verzeichnis für Chunks
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }
                
                WriteInfo($"Beginne Download von {chunks.Count} Chunks nach {tempDir}...");
                
                int completedChunks = 0;
                long totalBytes = 0;
                long totalSize = chunks.Sum(c => c.size);
                
                // Download-Fortschrittszähler
                var lastProgressUpdate = DateTime.Now;
                var startTime = DateTime.Now;
                
                foreach (var chunk in chunks)
                {
                    try
                    {
                        WriteInfo($"Lade Chunk {chunk.index + 1}/{chunks.Count} herunter...");
                        string chunkPath = Path.Combine(tempDir, $"chunk_{chunk.index}.bin");
                        
                        // Chunk herunterladen
                        using (var response = await _httpClient.GetAsync(chunk.url, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            
                            using (var fileStream = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            {
                                var buffer = new byte[8192]; // 8 KB Buffer
                                int bytesRead;
                                long chunkBytesRead = 0;
                                
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    
                                    // Aktualisiere Fortschritt
                                    chunkBytesRead += bytesRead;
                                    totalBytes += bytesRead;
                                    
                                    // Zeige Fortschritt etwa jede Sekunde an
                                    if ((DateTime.Now - lastProgressUpdate).TotalSeconds >= 1)
                                    {
                                        double overallProgress = (double)totalBytes / totalSize * 100;
                                        double chunkProgress = (double)chunkBytesRead / chunk.size * 100;
                                        
                                        // Berechne die geschätzte verbleibende Zeit
                                        var elapsed = DateTime.Now - startTime;
                                        var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / (totalBytes / (double)totalSize));
                                        var remaining = estimatedTotal - elapsed;
                                        
                                        string remainingStr = remaining.TotalHours >= 1 
                                            ? $"{remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s" 
                                            : remaining.TotalMinutes >= 1 
                                                ? $"{remaining.Minutes}m {remaining.Seconds}s" 
                                                : $"{remaining.Seconds}s";
                                        
                                        WriteInfo($"Fortschritt: {overallProgress:F1}% - Chunk {chunk.index + 1}: {chunkProgress:F1}% - Verbleibend: {remainingStr}");
                                        lastProgressUpdate = DateTime.Now;
                                    }
                                }
                            }
                        }
                        
                        completedChunks++;
                        WriteInfo($"Chunk {chunk.index + 1}/{chunks.Count} heruntergeladen ({completedChunks}/{chunks.Count})");
                    }
                    catch (Exception ex)
                    {
                        WriteError($"Fehler beim Herunterladen von Chunk {chunk.index + 1}: {ex.Message}");
                        throw;
                    }
                }
                
                WriteSuccess($"Alle {chunks.Count} Chunks heruntergeladen. Gesamtgröße: {FormatFileSize(totalBytes)}");
                
                // Chunks zusammenfügen
                WriteInfo($"Füge Chunks zu einer Datei zusammen: {outputFilePath}");
                using (var outputFile = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        string chunkPath = Path.Combine(tempDir, $"chunk_{i}.bin");
                        if (!File.Exists(chunkPath))
                        {
                            WriteError($"Chunk-Datei nicht gefunden: {chunkPath}");
                            throw new FileNotFoundException($"Chunk-Datei nicht gefunden: {chunkPath}");
                        }
                        
                        using (var chunkFile = new FileStream(chunkPath, FileMode.Open, FileAccess.Read))
                        {
                            await chunkFile.CopyToAsync(outputFile);
                        }
                        
                        // Fortschritt anzeigen
                        WriteInfo($"Chunk {i + 1}/{chunks.Count} zusammengefügt");
                    }
                }
                
                // Aufräumen
                try
                {
                    WriteInfo("Räume temporäre Dateien auf...");
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    WriteWarning($"Fehler beim Aufräumen der temporären Dateien: {ex.Message}");
                }
                
                // Fertig
                WriteSuccess($"Download von {gameInfo.Title} abgeschlossen: {outputFilePath}");
                WriteInfo($"Gesamtgröße: {FormatFileSize(new FileInfo(outputFilePath).Length)}");
            }
            catch (Exception ex)
            {
                WriteError($"Fehler beim lokalen Download: {ex.Message}");
                _logger.Error($"Fehler beim lokalen Download: {ex}");
                throw;
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        private long ParseFileSize(string sizeStr)
        {
            try
            {
                string[] parts = sizeStr.Split(' ');
                if (parts.Length != 2) return 0;
                
                if (!double.TryParse(parts[0], out double size)) return 0;
                
                string unit = parts[1].ToUpper();
                return unit switch
                {
                    "B" => (long)size,
                    "KB" => (long)(size * 1024),
                    "MB" => (long)(size * 1024 * 1024),
                    "GB" => (long)(size * 1024 * 1024 * 1024),
                    "TB" => (long)(size * 1024 * 1024 * 1024 * 1024),
                    _ => 0
                };
            }
            catch
            {
                return 0;
            }
        }
    }
}