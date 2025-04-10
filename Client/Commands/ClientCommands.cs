using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Client.Utils;
using LicenseSystem.Models;
using LicenseSystem.Services;

namespace Client.Commands
{
    public class DownloadCommand
    {
        private readonly LicenseClient _client;
        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;
        private readonly Logger? _logger;
        private TaskCompletionSource<string> _downloadInfoTcs = new();
        private bool _isWaitingForDownloadInfo = false;

        public DownloadCommand(LicenseClient client, Logger? logger)
        {
            _client = client;
            _httpClient = new HttpClient();
            _downloadDirectory = Path.Combine(Environment.CurrentDirectory, "downloads");
            _logger = logger;
            
            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
                _logger?.Debug($"Created download directory: {_downloadDirectory}");
            }
            
            _client.MessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            if (!_isWaitingForDownloadInfo)
                return;

            var message = e.Message;
            try
            {
                if (message.Content.StartsWith("{") && message.Content.Contains("\"GameTitle\""))
                {
                    _logger?.Debug("Received potential download information");
                    _downloadInfoTcs.TrySetResult(message.Content);
                    _isWaitingForDownloadInfo = false;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error processing download message: {ex.Message}");
                _downloadInfoTcs.TrySetException(ex);
                _isWaitingForDownloadInfo = false;
            }
        }

        public async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                _logger?.Warning("Missing game ID parameter");
                AnsiConsole.MarkupLine("[red]Du musst eine Spiel-ID angeben.[/]");
                AnsiConsole.MarkupLine("Verwendung: /download <game_id>");
                return;
            }

            string gameIdStr = args[0];
            
            try
            {
                _logger?.Info($"Requesting download information for game ID {gameIdStr}");
                AnsiConsole.MarkupLine($"[cyan]Frage Download-Informationen für Spiel-ID {gameIdStr} an...[/]");
                
                _downloadInfoTcs = new TaskCompletionSource<string>();
                _isWaitingForDownloadInfo = true;
                
                await _client.SendCommandAsync($"download {gameIdStr}");
                
                _logger?.Debug("Waiting for server response");
                AnsiConsole.MarkupLine("[cyan]Warte auf Antwort vom Server...[/]");
                
                var timeoutTask = Task.Delay(15000);
                var completedTask = await Task.WhenAny(_downloadInfoTcs.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _isWaitingForDownloadInfo = false;
                    _logger?.Error("Timeout waiting for server response");
                    AnsiConsole.MarkupLine("[red]Zeitüberschreitung beim Warten auf Serverantwort.[/]");
                    return;
                }
                
                // Parse the download information
                var downloadInfoJson = await _downloadInfoTcs.Task;
                var downloadInfo = JsonSerializer.Deserialize<DownloadInfo>(downloadInfoJson);
                
                if (downloadInfo == null)
                {
                    _logger?.Error("Failed to parse download information from server");
                    AnsiConsole.MarkupLine("[red]Keine gültigen Download-Informationen vom Server erhalten.[/]");
                    return;
                }
                
                string gameDirectory = Path.Combine(_downloadDirectory, downloadInfo.GameTitle);
                if (!Directory.Exists(gameDirectory))
                {
                    _logger?.Debug($"Creating game directory: {gameDirectory}");
                    Directory.CreateDirectory(gameDirectory);
                }
                
                _logger?.Info($"Received download info for \"{downloadInfo.GameTitle}\"");
                AnsiConsole.MarkupLine($"[green]Download-Informationen für \"{downloadInfo.GameTitle}\" erhalten.[/]");
                AnsiConsole.MarkupLine($"Datei-ID: {downloadInfo.FileId}");
                AnsiConsole.MarkupLine($"Anzahl Chunks: {downloadInfo.ChunkCount}");
                AnsiConsole.MarkupLine($"Gesamtgröße: {FormatFileSize(downloadInfo.TotalSize)}");
                
                if (!AnsiConsole.Confirm("Möchtest du den Download starten?"))
                {
                    _logger?.Info("Download canceled by user");
                    AnsiConsole.MarkupLine("[yellow]Download abgebrochen.[/]");
                    return;
                }
                
                _logger?.Info($"Starting download of {downloadInfo.ChunkCount} chunks for \"{downloadInfo.GameTitle}\"");
                await DownloadChunksAsync(downloadInfo, gameDirectory);
                
                _logger?.Info("Reassembling file from chunks");
                var outputFile = await ReassembleFileAsync(downloadInfo, gameDirectory);
                
                _logger?.Info($"Download of \"{downloadInfo.GameTitle}\" completed");
                AnsiConsole.MarkupLine($"[green]Download von \"{downloadInfo.GameTitle}\" abgeschlossen![/]");
                AnsiConsole.MarkupLine($"Datei gespeichert als: {outputFile}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error during download: {ex.Message}");
                AnsiConsole.MarkupLine($"[red]Fehler beim Download: {ex.Message}[/]");
            }
        }

        private async Task DownloadChunksAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            if (!Directory.Exists(chunksDirectory))
            {
                _logger?.Debug($"Creating chunks directory: {chunksDirectory}");
                Directory.CreateDirectory(chunksDirectory);
            }
            
            _logger?.Info("Starting chunk download process");
            await AnsiConsole.Progress()
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var overallTask = ctx.AddTask($"[green]Downloading {downloadInfo.GameTitle}[/]", 
                        maxValue: downloadInfo.Chunks.Count);
                    
                    foreach (var chunk in downloadInfo.Chunks)
                    {
                        _logger?.Debug($"Starting download of chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}");
                        var chunkTask = ctx.AddTask($"[cyan]Chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}[/]", 
                            maxValue: 100);
                        
                        string chunkPath = Path.Combine(chunksDirectory, $"chunk_{chunk.Index}.bin");
                        
                        try
                        {
                            using (var response = await _httpClient.GetAsync(chunk.Url, HttpCompletionOption.ResponseHeadersRead))
                            {
                                response.EnsureSuccessStatusCode();
                                
                                var totalBytes = response.Content.Headers.ContentLength ?? chunk.Size;
                                using (var fileStream = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                                using (var downloadStream = await response.Content.ReadAsStreamAsync())
                                {
                                    byte[] buffer = new byte[8192];
                                    long bytesRead = 0;
                                    int count;
                                    
                                    while ((count = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, count);
                                        bytesRead += count;
                                        
                                        double progress = (double)bytesRead / totalBytes * 100;
                                        chunkTask.Value = progress;
                                    }
                                }
                            }
                            
                            // Verify chunk hash if provided
                            if (!string.IsNullOrEmpty(chunk.Hash))
                            {
                                var fileHash = CalculateFileHash(chunkPath);
                                if (!string.Equals(fileHash, chunk.Hash, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger?.Warning($"Hash mismatch for chunk {chunk.Index}. Expected: {chunk.Hash}, Got: {fileHash}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error($"Error downloading chunk {chunk.Index}: {ex.Message}");
                            chunkTask.StopTask();
                            throw;
                        }
                        
                        chunkTask.Value = 100;
                        chunkTask.StopTask();
                        
                        overallTask.Increment(1);
                        _logger?.Debug($"Finished download of chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}");
                    }
                });
        }

        private async Task<string> ReassembleFileAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            _logger?.Info($"Reassembling file from {downloadInfo.ChunkCount} chunks");
            AnsiConsole.MarkupLine($"[cyan]Reassembliere Datei aus {downloadInfo.ChunkCount} Chunks...[/]");
            
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            
            string extension = ".bin";
            
            var firstChunk = downloadInfo.Chunks.FirstOrDefault(c => c.Index == 0);
            if (firstChunk != null && !string.IsNullOrEmpty(firstChunk.Url))
            {
                try {
                    var uri = new Uri(firstChunk.Url);
                    var fileName = Path.GetFileName(uri.AbsolutePath);
                    
                    if (fileName.Contains(".chunk"))
                    {
                        fileName = fileName.Replace(".chunk", "");
                    }
                    
                    var fileExt = Path.GetExtension(fileName);
                    if (!string.IsNullOrEmpty(fileExt))
                    {
                        extension = fileExt;
                        _logger?.Debug($"Extension extracted from chunk filename: {extension}");
                    }
                    
                    if (extension == ".bin" || extension == ".tmp" || extension == ".attachment")
                    {
                        int dotIndex = fileName.IndexOf('.');
                        if (dotIndex > 0 && dotIndex < fileName.Length - 1)
                        {
                            int lastUnderscore = fileName.LastIndexOf('_');
                            if (lastUnderscore > 0 && lastUnderscore < dotIndex)
                            {
                                string potentialExt = fileName.Substring(dotIndex);
                                if (!string.IsNullOrEmpty(potentialExt) && potentialExt.Length < 10) // Vernünftige Endungslänge
                                {
                                    extension = potentialExt;
                                    _logger?.Debug($"Extension extracted from filename part: {extension}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    _logger?.Warning($"Error extracting extension from URL: {ex.Message}");
                }
            }
            
            if (extension == ".bin")
            {
                var titleExt = Path.GetExtension(downloadInfo.GameTitle);
                if (!string.IsNullOrEmpty(titleExt))
                {
                    extension = titleExt;
                    _logger?.Debug($"Extension extracted from game title: {extension}");
                }
            }
            
            if (extension == ".bin")
            {
                try
                {
                    string firstChunkPath = Path.Combine(chunksDirectory, $"chunk_0.bin");
                    if (File.Exists(firstChunkPath))
                    {
                        using (var stream = new FileStream(firstChunkPath, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buffer = new byte[16];
                            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                            
                            if (read >= 4)
                            {
                                if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
                                {
                                    extension = ".jpg";
                                    _logger?.Debug("Detected JPG/JPEG from magic numbers");
                                }
                                else if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                                {
                                    extension = ".png";
                                    _logger?.Debug("Detected PNG from magic numbers");
                                }
                                else if (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04)
                                {
                                    extension = ".zip";
                                    _logger?.Debug("Detected ZIP from magic numbers");
                                }
                                else if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21)
                                {
                                    extension = ".rar";
                                    _logger?.Debug("Detected RAR from magic numbers");
                                }
                                else if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
                                {
                                    extension = ".pdf";
                                    _logger?.Debug("Detected PDF from magic numbers");
                                }
                                else if (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0x00 && buffer[3] == 0x20 && 
                                         buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
                                {
                                    extension = ".mp4";
                                    _logger?.Debug("Detected MP4 from magic numbers");
                                }
                                else if (buffer[0] == 0x1F && buffer[1] == 0x8B)
                                {
                                    extension = ".gz";
                                    _logger?.Debug("Detected GZIP from magic numbers");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning($"Error checking magic numbers: {ex.Message}");
                }
            }
            
            AnsiConsole.MarkupLine($"[cyan]Erkannte Dateiendung: {extension}[/]");
            
            string safeTitle = string.Join("_", downloadInfo.GameTitle.Split(Path.GetInvalidFileNameChars()));
            string outputFilePath = Path.Combine(outputDirectory, $"{safeTitle}{extension}");
            
            AnsiConsole.MarkupLine("[cyan]Erstelle Ausgabedatei...[/]");
            
            try
            {
                using (var outputFile = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    for (int i = 0; i < downloadInfo.ChunkCount; i++)
                    {
                        string chunkPath = Path.Combine(chunksDirectory, $"chunk_{i}.bin");
                        if (!File.Exists(chunkPath))
                        {
                            AnsiConsole.MarkupLine($"[red]Chunk-Datei nicht gefunden: {chunkPath}[/]");
                            throw new FileNotFoundException($"Chunk-Datei nicht gefunden: {chunkPath}");
                        }
                        
                        AnsiConsole.MarkupLine($"[cyan]Verarbeite Chunk {i+1}/{downloadInfo.ChunkCount}...[/]");
                        
                        using (var chunkFile = new FileStream(chunkPath, FileMode.Open, FileAccess.Read))
                        {
                            await chunkFile.CopyToAsync(outputFile);
                        }
                    }
                }
                
                AnsiConsole.MarkupLine("[cyan]Überprüfe Dateiintegrität...[/]");
                
                var fileInfo = new FileInfo(outputFilePath);
                if (fileInfo.Length != downloadInfo.TotalSize)
                {
                    _logger?.Warning($"File size mismatch: Expected {downloadInfo.TotalSize}, got {fileInfo.Length}");
                    AnsiConsole.MarkupLine($"[yellow]Warnung: Dateigröße stimmt nicht überein. Erwartet: {FormatFileSize(downloadInfo.TotalSize)}, Tatsächlich: {FormatFileSize(fileInfo.Length)}[/]");
                }
                
                bool deleteChunks = false;
                
                AnsiConsole.MarkupLine("[cyan]Möchtest du die heruntergeladenen Chunks löschen? (Standard: Ja)[/]");
                var input = Console.ReadLine()?.Trim().ToLower();
                deleteChunks = string.IsNullOrEmpty(input) || input == "y" || input == "j" || input == "yes" || input == "ja";
                
                if (deleteChunks)
                {
                    AnsiConsole.MarkupLine("[cyan]Lösche temporäre Chunk-Dateien...[/]");
                    try
                    {
                        Directory.Delete(chunksDirectory, true);
                        _logger?.Debug("Deleted chunks directory");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to delete chunks: {ex.Message}");
                        AnsiConsole.MarkupLine($"[yellow]Warnung: Konnte Chunks nicht löschen: {ex.Message}[/]");
                    }
                }
                
                _logger?.Info($"File successfully reassembled: {outputFilePath}");
                AnsiConsole.MarkupLine($"[green]Datei erfolgreich reassembliert: {outputFilePath}[/]");
                
                return outputFilePath;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error reassembling file: {ex.Message}");
                AnsiConsole.MarkupLine($"[red]Fehler beim Zusammenfügen der Datei: {ex.Message}[/]");
                throw;
            }
        }

        private string CalculateFileHash(string filePath)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
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
    }

    public class DownloadInfo
    {
        public int GameId { get; set; }
        public string GameTitle { get; set; }
        public string FileId { get; set; }
        public int ChunkCount { get; set; }
        public List<ChunkInfo> Chunks { get; set; }
        public long TotalSize { get; set; }
    }

    public class ChunkInfo
    {
        public int Index { get; set; }
        public string Id { get; set; }
        public long Size { get; set; }
        public string Url { get; set; }
        public string Hash { get; set; }
    }
}