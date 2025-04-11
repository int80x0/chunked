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
    public class GameListGenerator
    {
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly GameInfoService _gameInfoService;
        private readonly ulong _forumId;

        public GameListGenerator(
            IConfiguration config,
            Logger logger,
            GameInfoService gameInfoService)
        {
            _config = config;
            _logger = logger;
            _gameInfoService = gameInfoService;
            
            _forumId = _config.GetValue<ulong>("Forum:GamesForumId", 0);
        }

        public async Task<GameListResponse> GenerateGameListAsync(string category = "all")
        {
            try
            {
                _logger.Info($"Generating game list for category: {category}");
                
                var discordClient = Program.GetService<DiscordSocketClient>();
                if (discordClient == null)
                {
                    _logger.Error("Failed to get Discord client");
                    return new GameListResponse { Games = new List<GameListItem>(), TotalCount = 0, Category = category };
                }
                
                if (_forumId == 0)
                {
                    _logger.Error("No forum ID configured");
                    return new GameListResponse { Games = new List<GameListItem>(), TotalCount = 0, Category = category };
                }
                
                var channel = discordClient.GetChannel(_forumId);
                if (channel == null)
                {
                    _logger.Error($"Channel with ID {_forumId} not found");
                    return new GameListResponse { Games = new List<GameListItem>(), TotalCount = 0, Category = category };
                }
                
                var games = new List<GameListItem>();
                
                if (channel is IMessageChannel messageChannel)
                {
                    var messages = await messageChannel.GetMessagesAsync(200).FlattenAsync();
                    var gameGroupMessages = messages.Where(m => 
                        m.Embeds.Any(e => e.Title?.Contains("Version", StringComparison.OrdinalIgnoreCase) == true));
                    
                    foreach (var message in gameGroupMessages)
                    {
                        try
                        {
                            var versionEmbed = message.Embeds.FirstOrDefault(e => 
                                e.Title?.Contains("Version", StringComparison.OrdinalIgnoreCase) == true);
                            
                            if (versionEmbed == null)
                                continue;
                            
                            string title = versionEmbed.Title.Split('-').FirstOrDefault()?.Trim() ?? "Unknown Game";
                            
                            if (category != "all" && category != "newest" && category != "popular" && 
                                !title.Contains(category, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            int gameId = 0;
                            foreach (var field in versionEmbed.Fields)
                            {
                                if (field.Name.Contains("ID") || field.Value.Contains("ID:"))
                                {
                                    string idText = field.Value;
                                    var idPart = idText.Split(':').LastOrDefault()?.Trim();
                                    if (!string.IsNullOrEmpty(idPart))
                                    {
                                        int.TryParse(idPart.Replace(")", "").Trim(), out gameId);
                                    }
                                }
                            }
                            
                            string version = "1.0.0";
                            if (versionEmbed.Title.Contains('-'))
                            {
                                var versionPart = versionEmbed.Title.Split('-').LastOrDefault()?.Trim();
                                if (!string.IsNullOrEmpty(versionPart))
                                {
                                    version = versionPart.Replace("Version", "", StringComparison.OrdinalIgnoreCase).Trim();
                                }
                            }
                            
                            long fileSize = 0;
                            var fileSizeField = versionEmbed.Fields.FirstOrDefault(f => 
                                f.Name.Contains("Dateigröße", StringComparison.OrdinalIgnoreCase) || 
                                f.Name.Contains("Size", StringComparison.OrdinalIgnoreCase));
                                
                            if (fileSizeField != null)
                            {
                                var sizeString = fileSizeField.Value;
                                fileSize = ParseFileSize(sizeString);
                            }
                            
                            DateTime uploadDate = DateTime.Now;
                            var uploadDateField = versionEmbed.Fields.FirstOrDefault(f => 
                                f.Name.Contains("Hochgeladen", StringComparison.OrdinalIgnoreCase) || 
                                f.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase));
                                
                            if (uploadDateField != null)
                            {
                                DateTime.TryParse(uploadDateField.Value, out uploadDate);
                            }
                            else if (versionEmbed.Timestamp.HasValue)
                            {
                                uploadDate = versionEmbed.Timestamp.Value.DateTime;
                            }
                            
                            string description = string.Empty;
                            string genre = string.Empty;
                            string developer = string.Empty;
                            string thumbnailUrl = string.Empty;
                            
                            var gameMessages = messages.Where(m => 
                                m.Content.Contains(title, StringComparison.OrdinalIgnoreCase) && 
                                m.Embeds.Any(e => !e.Title.Contains("Version", StringComparison.OrdinalIgnoreCase) && 
                                e.Title.Contains(title, StringComparison.OrdinalIgnoreCase)));
                            
                            foreach (var gameMessage in gameMessages)
                            {
                                var gameEmbed = gameMessage.Embeds.FirstOrDefault(e => 
                                    !e.Title.Contains("Version", StringComparison.OrdinalIgnoreCase) && 
                                    e.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
                                    
                                if (gameEmbed != null)
                                {
                                    description = gameEmbed.Description;
                                    
                                    var genreField = gameEmbed.Fields.FirstOrDefault(f => 
                                        f.Name.Contains("Genre", StringComparison.OrdinalIgnoreCase));
                                        
                                    if (genreField != null)
                                    {
                                        genre = genreField.Value;
                                    }
                                    
                                    var developerField = gameEmbed.Fields.FirstOrDefault(f => 
                                        f.Name.Contains("Entwickler", StringComparison.OrdinalIgnoreCase) || 
                                        f.Name.Contains("Developer", StringComparison.OrdinalIgnoreCase));
                                        
                                    if (developerField != null)
                                    {
                                        developer = developerField.Value;
                                    }
                                    
                                    if (gameEmbed.Thumbnail.HasValue)
                                    {
                                        thumbnailUrl = gameEmbed.Thumbnail.Value.Url;
                                    }
                                    else if (gameEmbed.Image.HasValue)
                                    {
                                        thumbnailUrl = gameEmbed.Image.Value.Url;
                                    }
                                }
                            }
                            
                            // Try to get additional info from GameInfoService if we have a valid ID
                            GameInfo gameInfo = null;
                            if (gameId > 0)
                            {
                                try
                                {
                                    gameInfo = await _gameInfoService.GetGameInfoAsync(gameId.ToString());
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warning($"Failed to get game info for ID {gameId}: {ex.Message}");
                                }
                            }
                            
                            var gameListItem = new GameListItem
                            {
                                Id = gameId,
                                Title = !string.IsNullOrEmpty(title) ? title : gameInfo?.Title ?? "Unknown Game",
                                Description = !string.IsNullOrEmpty(description) ? description : gameInfo?.Description ?? string.Empty,
                                Size = fileSize,
                                Version = version,
                                UploadDate = uploadDate,
                                Developer = !string.IsNullOrEmpty(developer) ? developer : gameInfo?.Developer ?? string.Empty,
                                Genre = !string.IsNullOrEmpty(genre) ? genre : gameInfo?.Genre ?? string.Empty,
                                ThumbnailUrl = !string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl : gameInfo?.ThumbnailUrl ?? string.Empty
                            };
                            
                            games.Add(gameListItem);
                            _logger.Debug($"Added game to list: {gameListItem.Title} (ID: {gameListItem.Id})");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error processing message: {ex.Message}");
                        }
                    }
                }
                else
                {
                    _logger.Error($"Channel with ID {_forumId} is not a message channel");
                    return new GameListResponse { Games = new List<GameListItem>(), TotalCount = 0, Category = category };
                }
                
                switch (category.ToLower())
                {
                    case "newest":
                        games = games.OrderByDescending(g => g.UploadDate).ToList();
                        break;
                    case "popular":
                        games = games.OrderByDescending(g => g.Size).ToList();
                        break;
                    default:
                        games = games.OrderBy(g => g.Title).ToList();
                        break;
                }
                
                _logger.Info($"Generated game list with {games.Count} games for category: {category}");
                
                return new GameListResponse
                {
                    Games = games,
                    TotalCount = games.Count,
                    Category = category
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"Error generating game list: {ex.Message}");
                _logger.Debug($"Stack trace: {ex.StackTrace}");
                return new GameListResponse
                {
                    Games = new List<GameListItem>(),
                    TotalCount = 0,
                    Category = category
                };
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

    public class GameListResponse
    {
        public List<GameListItem> Games { get; set; } = new List<GameListItem>();
        public int TotalCount { get; set; }
        public string Category { get; set; }
    }

    public class GameListItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public long Size { get; set; }
        public string Version { get; set; }
        public DateTime UploadDate { get; set; }
        public string Developer { get; set; }
        public string Genre { get; set; }
        public string ThumbnailUrl { get; set; }
    }
}