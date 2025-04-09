using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Client.Utils;
using LicenseSystem.Services;

namespace Client.Commands
{
    public class DownloadCommand
    {
        private readonly LicenseClient _client;
        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;
        private readonly Logger? _logger;

        public DownloadCommand(LicenseClient client, Logger? logger)
        {
            _client = client;
            _httpClient = new HttpClient();
            _downloadDirectory = Path.Combine(Environment.CurrentDirectory, "downloads");
            _logger = logger;
            
            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
                _logger.Debug($"Created download directory: {_downloadDirectory}");
            }
        }

        public async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                _logger.Warning("Missing game ID parameter");
                AnsiConsole.MarkupLine("[red]Du musst eine Spiel-ID angeben.[/]");
                AnsiConsole.MarkupLine("Verwendung: /download <game_id>");
                return;
            }

            string gameIdStr = args[0];
            
            if (!int.TryParse(gameIdStr, out int gameId))
            {
                _logger.Warning($"Invalid game ID format: {gameIdStr}");
                AnsiConsole.MarkupLine($"[red]Ungültige Spiel-ID: {gameIdStr}[/]");
                return;
            }

            try
            {
                _logger.Info($"Requesting download information for game ID {gameId}");
                AnsiConsole.MarkupLine($"[cyan]Frage Download-Informationen für Spiel-ID {gameId} an...[/]");
                
                // Send a request to the server
                await _client.SendCommandAsync($"download {gameId}");
                
                // Wait for the response (this should be handled in an event handler)
                _logger.Debug("Waiting for server response");
                AnsiConsole.MarkupLine("[cyan]Warte auf Antwort vom Server...[/]");
                
                // In practice, you would use an event-based approach
                // For this example, we simulate a response
                var downloadInfo = await WaitForDownloadInfoAsync(gameId);
                
                if (downloadInfo == null)
                {
                    _logger.Error("No download information received from server");
                    AnsiConsole.MarkupLine("[red]Keine Download-Informationen vom Server erhalten.[/]");
                    return;
                }
                
                // Create a directory for the game
                string gameDirectory = Path.Combine(_downloadDirectory, downloadInfo.GameTitle);
                if (!Directory.Exists(gameDirectory))
                {
                    _logger.Debug($"Creating game directory: {gameDirectory}");
                    Directory.CreateDirectory(gameDirectory);
                }
                
                // Show download information
                _logger.Info($"Received download info for \"{downloadInfo.GameTitle}\"");
                AnsiConsole.MarkupLine($"[green]Download-Informationen für \"{downloadInfo.GameTitle}\" erhalten.[/]");
                AnsiConsole.MarkupLine($"Datei-ID: {downloadInfo.FileId}");
                AnsiConsole.MarkupLine($"Anzahl Chunks: {downloadInfo.ChunkCount}");
                AnsiConsole.MarkupLine($"Gesamtgröße: {FormatFileSize(downloadInfo.TotalSize)}");
                
                // Ask the user if they want to proceed
                if (!AnsiConsole.Confirm("Möchtest du den Download starten?"))
                {
                    _logger.Info("Download canceled by user");
                    AnsiConsole.MarkupLine("[yellow]Download abgebrochen.[/]");
                    return;
                }
                
                // Start downloading the chunks
                _logger.Info($"Starting download of {downloadInfo.ChunkCount} chunks for \"{downloadInfo.GameTitle}\"");
                await DownloadChunksAsync(downloadInfo, gameDirectory);
                
                // Reassemble the file
                _logger.Info("Reassembling file from chunks");
                await ReassembleFileAsync(downloadInfo, gameDirectory);
                
                _logger.Info($"Download of \"{downloadInfo.GameTitle}\" completed");
                AnsiConsole.MarkupLine($"[green]Download von \"{downloadInfo.GameTitle}\" abgeschlossen![/]");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during download: {ex.Message}");
                AnsiConsole.MarkupLine($"[red]Fehler beim Download: {ex.Message}[/]");
            }
        }

        private async Task<DownloadInfo> WaitForDownloadInfoAsync(int gameId)
        {
            // In a real implementation, you would wait for a message from the server
            // For this example, we simulate a short delay and return test data
            _logger.Debug("Simulating server response with download information");
            await Task.Delay(1500);
            
            return new DownloadInfo
            {
                GameId = gameId,
                GameTitle = gameId == 20952 ? "Grand Theft Auto V" : $"Game {gameId}",
                FileId = "1a2b3c4d5e6f7g8h9i0j",
                ChunkCount = 5,
                TotalSize = 5 * 1024 * 1024 * 1024L, // 5 GB
                Chunks = new List<ChunkInfo>
                {
                    new ChunkInfo { Index = 0, Id = "chunk1", Size = 1024 * 1024 * 1024, Url = "https://example.com/chunks/1" },
                    new ChunkInfo { Index = 1, Id = "chunk2", Size = 1024 * 1024 * 1024, Url = "https://example.com/chunks/2" },
                    new ChunkInfo { Index = 2, Id = "chunk3", Size = 1024 * 1024 * 1024, Url = "https://example.com/chunks/3" },
                    new ChunkInfo { Index = 3, Id = "chunk4", Size = 1024 * 1024 * 1024, Url = "https://example.com/chunks/4" },
                    new ChunkInfo { Index = 4, Id = "chunk5", Size = 1024 * 1024 * 1024, Url = "https://example.com/chunks/5" }
                }
            };
        }

        private async Task DownloadChunksAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            // Create a temporary directory for the chunks
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            if (!Directory.Exists(chunksDirectory))
            {
                _logger.Debug($"Creating chunks directory: {chunksDirectory}");
                Directory.CreateDirectory(chunksDirectory);
            }
            
            // Show a progress bar for the overall download
            _logger.Info("Starting chunk download process");
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
                        _logger.Debug($"Starting download of chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}");
                        var chunkTask = ctx.AddTask($"[cyan]Chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}[/]", 
                            maxValue: 100);
                        
                        string chunkPath = Path.Combine(chunksDirectory, $"chunk_{chunk.Index}.bin");
                        
                        // Simulate the download (in a real implementation, you would perform the actual download)
                        await SimulateChunkDownloadAsync(chunk, chunkPath, progress => chunkTask.Value = progress);
                        
                        chunkTask.Value = 100;
                        chunkTask.StopTask();
                        
                        overallTask.Increment(1);
                        _logger.Debug($"Finished download of chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}");
                    }
                });
        }

        private async Task SimulateChunkDownloadAsync(ChunkInfo chunk, string outputPath, Action<double> progressCallback)
        {
            _logger.Debug($"Simulating download of chunk {chunk.Index}, size: {FormatFileSize(chunk.Size)}");
            // Simulate a download with progress indication
            // In a real implementation, you would perform the actual download here
            
            // Create an empty file with the specified size (only for the simulation)
            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                // Simulate writing data to the file
                int bufferSize = 4096;
                byte[] buffer = new byte[bufferSize];
                long bytesWritten = 0;
                
                while (bytesWritten < chunk.Size)
                {
                    int bytesToWrite = (int)Math.Min(bufferSize, chunk.Size - bytesWritten);
                    
                    // In a real implementation, you would read data from the server here
                    // fileStream.Write(buffer, 0, bytesToWrite);
                    
                    bytesWritten += bytesToWrite;
                    
                    // Update progress
                    double progress = (double)bytesWritten / chunk.Size * 100;
                    progressCallback(progress);
                    
                    // Slow down the simulation a bit
                    await Task.Delay(50);
                }
            }
            
            // In the simulation, we don't use real files, so we just report that the file has been "downloaded"
            await Task.Delay(200);
            _logger.Debug($"Chunk {chunk.Index} simulation completed");
        }

        private async Task ReassembleFileAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            _logger.Info($"Reassembling file from {downloadInfo.ChunkCount} chunks");
            AnsiConsole.MarkupLine($"[cyan]Reassembliere Datei aus {downloadInfo.ChunkCount} Chunks...[/]");
            
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            string outputFilePath = Path.Combine(outputDirectory, $"{downloadInfo.GameTitle}.bin");
            
            await AnsiConsole.Status()
                .StartAsync("Reassembliere Datei...", async ctx =>
                {
                    // In a real implementation, you would perform the actual reassembly here
                    // Simulate the reassembly
                    _logger.Debug($"Creating output file: {outputFilePath}");
                    ctx.Status($"Erstelle Ausgabedatei: {outputFilePath}");
                    await Task.Delay(1000);
                    
                    _logger.Debug("Connecting chunks");
                    ctx.Status("Verbinde Chunks...");
                    await Task.Delay(2000);
                    
                    _logger.Debug("Verifying file integrity");
                    ctx.Status("Überprüfe Dateiintegrität...");
                    await Task.Delay(1000);
                });
            
            _logger.Info($"File successfully reassembled: {outputFilePath}");
            AnsiConsole.MarkupLine($"[green]Datei erfolgreich reassembliert: {outputFilePath}[/]");
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
    }
}