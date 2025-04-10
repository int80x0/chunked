using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Server.Game;
using Server.Utils;

namespace Server.Server
{
    public class DownloadInfoGenerator
    {
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ChunkManager _chunkManager;
        private readonly ulong _forumId;

        public DownloadInfoGenerator(
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService,
            ChunkManager chunkManager)
        {
            _config = config;
            _logger = logger;
            _gameInfoService = gameInfoService;
            _chunkManager = chunkManager;
            
            _forumId = _config.GetValue<ulong>("Forum:GamesForumId", 0);
        }

        public async Task<DownloadInfo> GenerateDownloadInfoAsync(GameInfo gameInfo)
        {
            try
            {
                _logger.Info($"Generating download info for game: {gameInfo.Title} (ID: {gameInfo.Id})");
                
                // Get Discord client through DI if possible, otherwise create a service to access it
                var discordClient = Program.GetService<DiscordSocketClient>();
                if (discordClient == null)
                {
                    _logger.Error("Failed to get Discord client");
                    return null;
                }
                
                if (_forumId == 0)
                {
                    _logger.Error("No forum ID configured");
                    return null;
                }
                
                var channel = discordClient.GetChannel(_forumId);
                if (channel == null)
                {
                    _logger.Error($"Channel with ID {_forumId} not found");
                    return null;
                }
                
                IEnumerable<IMessage> allMessages;
                
                // Get messages from forum or text channel
                if (channel is SocketForumChannel forumChannel)
                {
                    var threads = await forumChannel.GetActiveThreadsAsync();
                    var gameThread = threads.FirstOrDefault(t => 
                        t.Name.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase));
                    
                    if (gameThread == null)
                    {
                        _logger.Warning($"No thread found for game '{gameInfo.Title}'");
                        return null;
                    }
                    
                    _logger.Info($"Found thread for {gameInfo.Title} (ID: {gameThread.Id})");
                    
                    var messageChannel = gameThread as IMessageChannel;
                    if (messageChannel == null)
                    {
                        _logger.Error("Failed to cast thread to message channel");
                        return null;
                    }
                    
                    allMessages = await messageChannel.GetMessagesAsync(200).FlattenAsync();
                }
                else if (channel is IMessageChannel textChannel)
                {
                    var messages = await textChannel.GetMessagesAsync(200).FlattenAsync();
                    allMessages = messages.Where(m => 
                        m.Content?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true || 
                        m.Embeds.Any(e => e.Title?.Contains(gameInfo.Title, StringComparison.OrdinalIgnoreCase) == true));
                }
                else
                {
                    _logger.Error($"Channel with ID {_forumId} is neither a forum nor a text channel");
                    return null;
                }
                
                // Find file ID from version embed
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
                    _logger.Warning("No file ID found");
                    return null;
                }
                
                _logger.Info($"Found file ID: {fileId}");
                
                // Find chunks
                var chunkMessages = allMessages
                    .Where(m => m.Embeds.Any(e => e.Title?.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase) == true &&
                                              e.Footer?.Text?.Contains(fileId) == true))
                    .ToList();
                
                if (chunkMessages.Count == 0)
                {
                    _logger.Warning("No chunks found for this game");
                    return null;
                }
                
                _logger.Info($"Found {chunkMessages.Count} chunks for {gameInfo.Title}");
                
                // Extract chunk information
                var chunks = new List<ChunkInfo>();
                foreach (var message in chunkMessages)
                {
                    if (message.Attachments.Count == 0) continue;
                    
                    foreach (var embed in message.Embeds)
                    {
                        if (embed.Title != null && embed.Title.StartsWith("Chunk"))
                        {
                            string[] parts = embed.Title.Split(' ')[1].Split('/');
                            if (int.TryParse(parts[0], out int index))
                            {
                                string chunkId = null;
                                long size = 0;
                                string hash = null;
                                
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
                                
                                var attachment = message.Attachments.First();
                                string url = attachment.Url;
                                
                                if (!string.IsNullOrEmpty(chunkId) && !string.IsNullOrEmpty(url))
                                {
                                    chunks.Add(new ChunkInfo
                                    {
                                        Index = index - 1,  // Adjust to 0-based index
                                        Id = chunkId,
                                        Size = size,
                                        Url = url,
                                        Hash = hash
                                    });
                                }
                            }
                        }
                    }
                }
                
                // Sort chunks by index
                chunks = chunks.OrderBy(c => c.Index).ToList();
                
                if (chunks.Count == 0)
                {
                    _logger.Error("Failed to extract chunk information");
                    return null;
                }
                
                _logger.Info($"Successfully extracted {chunks.Count} chunks");
                
                // Create download info
                var downloadInfo = new DownloadInfo
                {
                    GameId = gameInfo.Id,
                    GameTitle = gameInfo.Title,
                    FileId = fileId,
                    ChunkCount = chunks.Count,
                    Chunks = chunks,
                    TotalSize = chunks.Sum(c => c.Size)
                };
                
                return downloadInfo;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error generating download info: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                return null;
            }
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