using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Server.Utils;

namespace Server.Game
{
    public class FileChunk
    {
        public string Id { get; set; }
        public string FilePath { get; set; }
        public long Size { get; set; }
        public int Index { get; set; }
        public string Hash { get; set; }
        public string OriginalFileName { get; set; }
        public string GameId { get; set; }
    }

    public class ChunkManager
    {
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        
        private readonly string _chunksDirectory;
        private readonly long _defaultChunkSize;
        private readonly string _downloadBaseUrl;

        public ChunkManager(IConfiguration config, Logger logger)
        {
            _config = config;
            _logger = logger;
            
            _chunksDirectory = _config.GetValue<string>("Game:ChunksDirectory", "data/chunks");
            _defaultChunkSize = _config.GetValue<long>("Game:DefaultChunkSize", 8 * 1024 * 1024); // 8 MB Standard
            _downloadBaseUrl = _config.GetValue<string>("Game:DownloadBaseUrl", "http://localhost:5000/api/chunks");
            
            if (!Directory.Exists(_chunksDirectory))
            {
                Directory.CreateDirectory(_chunksDirectory);
            }
            
            _logger.Info($"ChunkManager initialisiert. Chunk-Verzeichnis: {_chunksDirectory}, Chunk-Größe: {FormatFileSize(_defaultChunkSize)}");
        }

        public int CalculateChunkCount(long fileSize)
        {
            return (int)Math.Ceiling((double)fileSize / _defaultChunkSize);
        }

        public async Task<List<FileChunk>> CreateChunksAsync(string filePath)
        {
            var result = new List<FileChunk>();
            
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException($"Die Datei '{filePath}' wurde nicht gefunden.");
                }
                
                string fileId = Guid.NewGuid().ToString("N");
                string gameName = Path.GetFileNameWithoutExtension(filePath);
                string fileExtension = Path.GetExtension(filePath);
                
                // Erstelle ein Verzeichnis für diese Datei
                string chunkDir = Path.Combine(_chunksDirectory, fileId);
                Directory.CreateDirectory(chunkDir);
                
                _logger.Info($"Teile Datei '{fileInfo.Name}' ({FormatFileSize(fileInfo.Length)}) in Chunks von {FormatFileSize(_defaultChunkSize)}...");
                
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var md5 = MD5.Create();
                
                byte[] buffer = new byte[_defaultChunkSize];
                int bytesRead;
                long totalBytesRead = 0;
                int chunkIndex = 0;
                
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Berechne Hash für diesen Chunk
                    byte[] hash = md5.ComputeHash(buffer, 0, bytesRead);
                    string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    
                    // Erstelle Chunk-Datei
                    string chunkFileName = $"{gameName}_{chunkIndex}{fileExtension}.chunk";
                    string chunkPath = Path.Combine(chunkDir, chunkFileName);
                    
                    using (var chunkStream = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                    {
                        await chunkStream.WriteAsync(buffer, 0, bytesRead);
                    }
                    
                    // Erstelle Chunk-Objekt
                    var chunk = new FileChunk
                    {
                        Id = $"{fileId}_{chunkIndex}",
                        FilePath = chunkPath,
                        Size = bytesRead,
                        Index = chunkIndex,
                        Hash = hashString,
                        OriginalFileName = fileInfo.Name,
                        GameId = gameName
                    };
                    
                    result.Add(chunk);
                    
                    totalBytesRead += bytesRead;
                    chunkIndex++;
                    
                    _logger.Debug($"Chunk {chunkIndex} erstellt: {chunkFileName} ({FormatFileSize(bytesRead)})");
                }
                
                // Erstelle eine Manifest-Datei
                await CreateManifestAsync(chunkDir, result, fileInfo);
                
                _logger.Info($"Datei in {result.Count} Chunks aufgeteilt. Total: {FormatFileSize(totalBytesRead)}");
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Erstellen der Chunks: {ex.Message}");
                throw;
            }
        }

        private async Task CreateManifestAsync(string chunkDir, List<FileChunk> chunks, FileInfo originalFile)
        {
            try
            {
                var manifest = new
                {
                    FileName = originalFile.Name,
                    FileSize = originalFile.Length,
                    LastModified = originalFile.LastWriteTime,
                    ChunkCount = chunks.Count,
                    ChunkSize = _defaultChunkSize,
                    Chunks = chunks.Select(c => new
                    {
                        c.Id,
                        c.Index,
                        c.Size,
                        c.Hash,
                        FileName = Path.GetFileName(c.FilePath)
                    }).ToList(),
                    DownloadBaseUrl = _downloadBaseUrl
                };
                
                string manifestPath = Path.Combine(chunkDir, "manifest.json");
                string json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                
                await File.WriteAllTextAsync(manifestPath, json);
                
                _logger.Info($"Manifest-Datei erstellt: {manifestPath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Erstellen der Manifest-Datei: {ex.Message}");
            }
        }

        public async Task<FileInfo> ReassembleChunksAsync(string fileId, string outputDirectory)
        {
            try
            {
                string chunkDir = Path.Combine(_chunksDirectory, fileId);
                if (!Directory.Exists(chunkDir))
                {
                    throw new DirectoryNotFoundException($"Chunk-Verzeichnis für Datei-ID '{fileId}' nicht gefunden.");
                }
                
                string manifestPath = Path.Combine(chunkDir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException($"Manifest-Datei für Datei-ID '{fileId}' nicht gefunden.");
                }
                
                string json = await File.ReadAllTextAsync(manifestPath);
                var manifest = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                
                string fileName = manifest.GetProperty("FileName").GetString();
                int chunkCount = manifest.GetProperty("ChunkCount").GetInt32();
                
                string outputPath = Path.Combine(outputDirectory, fileName);
                
                _logger.Info($"Reassembliere Datei '{fileName}' aus {chunkCount} Chunks...");
                
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                
                // Iteriere über alle Chunks in der richtigen Reihenfolge
                var chunks = manifest.GetProperty("Chunks");
                for (int i = 0; i < chunkCount; i++)
                {
                    // Finde den Chunk mit dem aktuellen Index
                    string chunkFileName = null;
                    foreach (var chunk in chunks.EnumerateArray())
                    {
                        if (chunk.GetProperty("Index").GetInt32() == i)
                        {
                            chunkFileName = chunk.GetProperty("FileName").GetString();
                            break;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(chunkFileName))
                    {
                        throw new FileNotFoundException($"Chunk mit Index {i} für Datei-ID '{fileId}' nicht gefunden.");
                    }
                    
                    string chunkPath = Path.Combine(chunkDir, chunkFileName);
                    if (!File.Exists(chunkPath))
                    {
                        throw new FileNotFoundException($"Chunk-Datei '{chunkFileName}' für Datei-ID '{fileId}' nicht gefunden.");
                    }
                    
                    // Lese den Chunk und schreibe ihn in die Ausgabedatei
                    using var chunkStream = new FileStream(chunkPath, FileMode.Open, FileAccess.Read);
                    await chunkStream.CopyToAsync(outputStream);
                    
                    _logger.Debug($"Chunk {i + 1}/{chunkCount} reassembliert: {chunkFileName}");
                }
                
                _logger.Info($"Datei '{fileName}' erfolgreich reassembliert: {outputPath}");
                
                return new FileInfo(outputPath);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Reassemblieren der Chunks: {ex.Message}");
                throw;
            }
        }
        
        public string FormatFileSize(long bytes)
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
}