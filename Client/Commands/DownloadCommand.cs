using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Client.Console;
using Client.Models;
using Client.Utils;
using LicenseSystem.Services;
using Spectre.Console;

namespace Client.Commands
{
    public class DownloadCommand : ConsoleCommand
    {
        private readonly LicenseClient _client;
        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;
        private readonly Logger _logger;
        private TaskCompletionSource<string> _downloadInfoTcs = new();
        private bool _isWaitingForDownloadInfo = false;

        public DownloadCommand(LicenseClient client, Logger logger) 
            : base(
                "download", 
                "Downloads a game by its ID or name", 
                "<name>", 
                "/download \"Grand Theft Auto V\"", 
                GetAutocompleteSuggestionsAsync)
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
            
            _client.MessageReceived += OnMessageReceived!;
        }

        private static Task<List<string>> GetAutocompleteSuggestionsAsync(string[] args)
        {
            return Task.FromResult<List<string>>(args.Length == 0 ?
                [
                    "Grand Theft Auto V", "The Witcher 3", "Fallout 4", "Cyberpunk 2077"
                ]
                : []);
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (!_isWaitingForDownloadInfo)
                return;

            var message = e.Message;
            try
            {
                if (!message.Content.StartsWith('{') || !message.Content.Contains("\"GameTitle\"")) return;
                _logger?.Debug("Received potential download information");
                _downloadInfoTcs.TrySetResult(message.Content);
                _isWaitingForDownloadInfo = false;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error processing download message: {ex.Message}");
                _downloadInfoTcs.TrySetException(ex);
                _isWaitingForDownloadInfo = false;
            }
        }

        public override async Task ExecuteAsync(string[] args)
        {
            if (args.Length < 1)
            {
                WriteError("You must specify a game ID or name.");
                WriteInfo($"Usage: /{Name} {Usage}");
                return;
            }

            var gameIdOrName = args[0];
            
            try
            {
                _logger?.Info($"Requesting download information for game: {gameIdOrName}");
                WriteInfo($"Requesting download information for game: {gameIdOrName}...");
                
                _downloadInfoTcs = new TaskCompletionSource<string>();
                _isWaitingForDownloadInfo = true;
                
                await _client.SendCommandAsync($"download {gameIdOrName}");
                
                _logger?.Debug("Waiting for server response");
                WriteInfo("Waiting for server response...");
                
                var timeoutTask = Task.Delay(15000);
                var completedTask = await Task.WhenAny(_downloadInfoTcs.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _isWaitingForDownloadInfo = false;
                    WriteError("Timeout waiting for server response.");
                    return;
                }
                
                var downloadInfoJson = await _downloadInfoTcs.Task;
                var downloadInfo = JsonSerializer.Deserialize<DownloadInfo>(downloadInfoJson);
                
                if (downloadInfo == null)
                {
                    WriteError("Failed to parse download information from server.");
                    return;
                }
                
                var gameDirectory = Path.Combine(_downloadDirectory, downloadInfo.GameTitle);
                if (!Directory.Exists(gameDirectory))
                {
                    _logger?.Debug($"Creating game directory: {gameDirectory}");
                    Directory.CreateDirectory(gameDirectory);
                }
                
                _logger?.Info($"Received download info for \"{downloadInfo.GameTitle}\"");
                WriteSuccess($"Download information received for \"{downloadInfo.GameTitle}\"");
                WriteInfo($"File ID: {downloadInfo.FileId}");
                WriteInfo($"Number of chunks: {downloadInfo.ChunkCount}");
                WriteInfo($"Total size: {FormatFileSize(downloadInfo.TotalSize)}");
                
                if (!await ConfirmActionAsync("Do you want to start the download?"))
                {
                    _logger?.Info("Download canceled by user");
                    WriteInfo("Download canceled.");
                    return;
                }
                
                _logger?.Info($"Starting download of {downloadInfo.ChunkCount} chunks for \"{downloadInfo.GameTitle}\"");
                await DownloadChunksAsync(downloadInfo, gameDirectory);
                
                _logger?.Info("Reassembling file from chunks");
                var outputFile = await ReassembleFileAsync(downloadInfo, gameDirectory);
                
                _logger?.Info($"Download of \"{downloadInfo.GameTitle}\" completed");
                WriteSuccess($"Download of \"{downloadInfo.GameTitle}\" completed!");
                WriteInfo($"File saved as: {outputFile}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error during download: {ex.Message}");
                WriteError($"Error during download: {ex.Message}");
            }
        }

        private async Task DownloadChunksAsync(DownloadInfo downloadInfo, string outputDirectory)
        {
            var chunksDirectory = Path.Combine(outputDirectory, "chunks");
            if (!Directory.Exists(chunksDirectory))
            {
                _logger?.Debug($"Creating chunks directory: {chunksDirectory}");
                Directory.CreateDirectory(chunksDirectory);
            }
            
            _logger?.Info("Starting chunk download process");
            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var overallTask = ctx.AddTask($"[green]Downloading {downloadInfo.GameTitle}[/]", 
                        maxValue: downloadInfo.Chunks.Count);
                    
                    foreach (var chunk in downloadInfo.Chunks)
                    {
                        _logger?.Debug($"Starting download of chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}");
                        var chunkTask = ctx.AddTask($"[cyan]Chunk {chunk.Index + 1}/{downloadInfo.ChunkCount}[/]", 
                            maxValue: 100);
                        
                        var chunkPath = Path.Combine(chunksDirectory, $"chunk_{chunk.Index}.bin");
                        
                        try
                        {
                            using (var response = await _httpClient.GetAsync(chunk.Url, HttpCompletionOption.ResponseHeadersRead))
                            {
                                response.EnsureSuccessStatusCode();
                                
                                var totalBytes = response.Content.Headers.ContentLength ?? chunk.Size;
                                using (var fileStream = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                                using (var downloadStream = await response.Content.ReadAsStreamAsync())
                                {
                                    var buffer = new byte[8192];
                                    long bytesRead = 0;
                                    int count;
                                    
                                    while ((count = await downloadStream.ReadAsync(buffer)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer.AsMemory(0, count));
                                        bytesRead += count;
                                        
                                        var progress = (double)bytesRead / totalBytes * 100;
                                        chunkTask.Value = progress;
                                    }
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(chunk.Hash))
                            {
                                var fileHash = CalculateFileHash(chunkPath);
                                if (!string.Equals(fileHash, chunk.Hash, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger?.Warning($"Hash mismatch for chunk {chunk.Index}. Expected: {chunk.Hash}, Got: {fileHash}");
                                    WriteWarning($"Hash mismatch for chunk {chunk.Index}. The file might be corrupted.");
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
            WriteInfo($"Reassembling file from {downloadInfo.ChunkCount} chunks...");
            
            var chunksDirectory = Path.Combine(outputDirectory, "chunks");
            
            var extension = ".bin";
            
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
                }
                catch (Exception ex) {
                    _logger?.Warning($"Error extracting extension from URL: {ex.Message}");
                }
            }
            
            WriteInfo($"Detected file extension: {extension}");
            
            var safeTitle = string.Join("_", downloadInfo.GameTitle.Split(Path.GetInvalidFileNameChars()));
            var outputFilePath = Path.Combine(outputDirectory, $"{safeTitle}{extension}");
            
            WriteInfo("Creating output file...");
            
            try
            {
                await using (var outputFile = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    for (var i = 0; i < downloadInfo.ChunkCount; i++)
                    {
                        var chunkPath = Path.Combine(chunksDirectory, $"chunk_{i}.bin");
                        if (!File.Exists(chunkPath))
                        {
                            WriteError($"Chunk file not found: {chunkPath}");
                            throw new FileNotFoundException($"Chunk file not found: {chunkPath}");
                        }
                        
                        WriteInfo($"Processing chunk {i+1}/{downloadInfo.ChunkCount}...");

                        await using var chunkFile = new FileStream(chunkPath, FileMode.Open, FileAccess.Read);
                        await chunkFile.CopyToAsync(outputFile);
                    }
                }
                
                WriteInfo("Verifying file integrity...");
                
                var fileInfo = new FileInfo(outputFilePath);
                if (fileInfo.Length != downloadInfo.TotalSize)
                {
                    _logger?.Warning($"File size mismatch: Expected {downloadInfo.TotalSize}, got {fileInfo.Length}");
                    WriteWarning($"File size mismatch: Expected {FormatFileSize(downloadInfo.TotalSize)}, got {FormatFileSize(fileInfo.Length)}");
                }
                
                var deleteChunks = await ConfirmActionAsync("Do you want to delete the downloaded chunks? (Default: Yes)");
                
                if (deleteChunks)
                {
                    WriteInfo("Deleting temporary chunk files...");
                    try
                    {
                        Directory.Delete(chunksDirectory, true);
                        _logger?.Debug("Deleted chunks directory");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to delete chunks: {ex.Message}");
                        WriteWarning($"Could not delete chunks: {ex.Message}");
                    }
                }
                
                _logger?.Info($"File successfully reassembled: {outputFilePath}");
                WriteSuccess($"File successfully reassembled: {outputFilePath}");
                
                return outputFilePath;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error reassembling file: {ex.Message}");
                WriteError($"Error reassembling file: {ex.Message}");
                throw;
            }
        }

        private static string CalculateFileHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return Convert.ToHexStringLower(hash);
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            var suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }
}