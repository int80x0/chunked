using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LicenseSystem.Services;

namespace Client.Commands
{
    public class DownloadCommand
    {
        private readonly LicenseClient _client;
        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;

        public DownloadCommand(LicenseClient client)
        {
            _client = client;
            _httpClient = new HttpClient();
            _downloadDirectory = Path.Combine(Environment.CurrentDirectory, "downloads");
            
            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
            }
        }

        public async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                AnsiConsole.MarkupLine("[red]Du musst eine Spiel-ID angeben.[/]");
                AnsiConsole.MarkupLine("Verwendung: /download <game_id>");
                return;
            }

            string gameIdStr = args[0];
            
            if (!int.TryParse(gameIdStr, out int gameId))
            {
                AnsiConsole.MarkupLine($"[red]Ungültige Spiel-ID: {gameIdStr}[/]");
                return;
            }

            try
            {
                AnsiConsole.MarkupLine($"[cyan]Frage Download-Informationen für Spiel-ID {gameId} an...[/]");
                
                // Sende eine Anfrage an den Server
                await _client.SendCommandAsync($"download {gameId}");
                
                // Warte auf die Antwort (dies sollte in einem Event-Handler behandelt werden)
                AnsiConsole.MarkupLine("[cyan]Warte auf Antwort vom Server...[/]");
                
                // In der Praxis würdest du hier einen Event-basierten Ansatz verwenden
                // Für dieses Beispiel simulieren wir eine Antwort
                var downloadInfo = await WaitForDownloadInfoAsync(gameId);
                
                if (downloadInfo == null)
                {
                    AnsiConsole.MarkupLine("[red]Keine Download-Informationen vom Server erhalten.[/]");
                    return;
                }
                
                // Erstelle ein Verzeichnis für das Spiel
                string gameDirectory = Path.Combine(_downloadDirectory, downloadInfo.GameTitle);
                if (!Directory.Exists(gameDirectory))
                {
                    Directory.CreateDirectory(gameDirectory);
                }
                
                // Zeige Download-Informationen
                AnsiConsole.MarkupLine($"[green]Download-Informationen für \"{downloadInfo.GameTitle}\" erhalten.[/]");
                AnsiConsole.MarkupLine($"Datei-ID: {downloadInfo.FileId}");
                AnsiConsole.MarkupLine($"Anzahl Chunks: {downloadInfo.ChunkCount}");
                AnsiConsole.MarkupLine($"Gesamtgröße: {FormatFileSize(downloadInfo.TotalSize)}");
                
                // Frage den Benutzer, ob er fortfahren möchte
                if (!AnsiConsole.Confirm("Möchtest du den Download starten?"))
                {
                    AnsiConsole.MarkupLine("[yellow]Download abgebrochen.[/]");
                    return;
                }
                
                // Starte den Download der Chunks
                await DownloadChunksAsync(downloadInfo, gameDirectory);
                
                // Reassembliere die Datei
                await ReassembleFileAsync(downloadInfo, gameDirectory);
                
                AnsiConsole.MarkupLine($"[green]Download von \"{downloadInfo.GameTitle}\" abgeschlossen![/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Fehler beim Download: {ex.Message}[/]");
            }
        }

        private async Task<DownloadInfo> WaitForDownloadInfoAsync(int gameId)
        {
            // In einer echten Implementierung würdest du auf eine Nachricht vom Server warten
            // Für dieses Beispiel simulieren wir eine kurze Verzögerung und geben Testdaten zurück
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
            // Erstelle ein temporäres Verzeichnis für die Chunks
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            if (!Directory.Exists(chunksDirectory))
            {
                Directory.CreateDirectory(chunksDirectory);
            }
            
            // Zeige einen Fortschrittsbalken für den gesamten Download
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
                        var chunkTask = ctx.AddTask($"[cyan]Chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}[/]", 
                            maxValue: 100);
                        
                        string chunkPath = Path.Combine(chunksDirectory, $"chunk_{chunk.Index}.bin");
                        
                        // Simuliere den Download (in einer echten Implementierung würdest du den tatsächlichen Download durchführen)
                        await SimulateChunkDownloadAsync(chunk, chunkPath, progress => chunkTask.Value = progress);
                        
                        chunkTask.Value = 100;
                        chunkTask.StopTask();
                        
                        overallTask.Increment(1);
                    }
                });
        }

        private async Task SimulateChunkDownloadAsync(ChunkInfo chunk, string outputPath, Action<double> progressCallback)
        {
            // Simuliere einen Download mit Fortschrittsanzeige
            // In einer echten Implementierung würdest du hier den tatsächlichen Download durchführen
            
            // Erstelle eine leere Datei mit der angegebenen Größe (nur für die Simulation)
            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                // Simuliere das Schreiben von Daten in die Datei
                int bufferSize = 4096;
                byte[] buffer = new byte[bufferSize];
                long bytesWritten = 0;
                
                while (bytesWritten < chunk.Size)
                {
                    int bytesToWrite = (int)Math.Min(bufferSize, chunk.Size - bytesWritten);
                    
                    // In einer echten Implementierung würdest du hier Daten vom Server lesen
                    // fileStream.Write(buffer, 0, bytesToWrite);
                    
                    bytesWritten += bytesToWrite;
                    
                    // Aktualisiere den Fortschritt
                    double progress = (double)bytesWritten / chunk.Size * 100;
                    progressCallback(progress);
                    
                    // Verlangsame die Simulation etwas
                    await Task.Delay(50);
                }
            }
            
            // In der Simulation verwenden wir keine echten Dateien, daher melden wir einfach,
            // dass die Datei "heruntergeladen" wurde
            await Task.Delay(200);
        }

        private async Task ReassembleFileAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            AnsiConsole.MarkupLine($"[cyan]Reassembliere Datei aus {downloadInfo.ChunkCount} Chunks...[/]");
            
            string chunksDirectory = Path.Combine(outputDirectory, "chunks");
            string outputFilePath = Path.Combine(outputDirectory, $"{downloadInfo.GameTitle}.bin");
            
            await AnsiConsole.Status()
                .StartAsync("Reassembliere Datei...", async ctx =>
                {
                    // In einer echten Implementierung würdest du hier die tatsächliche Reassemblierung durchführen
                    // Simuliere die Reassemblierung
                    ctx.Status($"Erstelle Ausgabedatei: {outputFilePath}");
                    await Task.Delay(1000);
                    
                    ctx.Status("Verbinde Chunks...");
                    await Task.Delay(2000);
                    
                    ctx.Status("Überprüfe Dateiintegrität...");
                    await Task.Delay(1000);
                });
            
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