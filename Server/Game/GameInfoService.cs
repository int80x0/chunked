using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Server.Utils;

namespace Server.Game
{
    public class GameInfo
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string ThumbnailUrl { get; set; }
        public string CoverUrl { get; set; }
        public string Genre { get; set; }
        public string Developer { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string Platform { get; set; }
        public Dictionary<string, string> ExtraInfo { get; set; } = new Dictionary<string, string>();
    }

    public class GameInfoService
    {
        private readonly IConfiguration _config;
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _localCacheDir;
        
        private string _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        
        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7);
        
        
        private static readonly Dictionary<int, int> LegacyIdToIgdbId = new Dictionary<int, int>
        {
            { 20952, 1020 },   
            { 2282, 1942 },    
            { 1234, 472 },     
            { 5678, 121 }      
            
        };

        public GameInfoService(IConfiguration config, Logger logger, IMemoryCache cache)
        {
            _config = config;
            _logger = logger;
            _cache = cache;
            
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ChunkyBot/1.0");
            
            _clientId = _config["IGDB:ClientId"];
            _clientSecret = _config["IGDB:ClientSecret"];
            _localCacheDir = _config.GetValue<string>("Game:CacheDirectory", "data/game_cache");
            
            if (!Directory.Exists(_localCacheDir))
            {
                Directory.CreateDirectory(_localCacheDir);
            }
            
            _logger.Info($"GameInfoService initialisiert. Cache-Verzeichnis: {_localCacheDir}");
        }

        
        
        
        public async Task<GameInfo> GetGameInfoAsync(string nameOrId)
        {
            
            if (int.TryParse(nameOrId, out int parsedId))
            {
                return await GetGameInfoByIdAsync(parsedId);
            }
            else
            {
                
                return await GetGameInfoByNameAsync(nameOrId);
            }
        }

        
        
        
        public async Task<GameInfo> GetGameInfoByIdAsync(int gameId)
        {
            
            int igdbId = gameId;
            if (LegacyIdToIgdbId.TryGetValue(gameId, out int mappedId))
            {
                _logger.Debug($"Alte ID {gameId} zu IGDB ID {mappedId} konvertiert");
                igdbId = mappedId;
            }
            
            
            return await FetchAndCacheGameInfoAsync(igdbId.ToString(), async () =>
            {
                await EnsureAccessTokenAsync();
                return await FetchGameFromApiByIdAsync(igdbId);
            });
        }

        
        
        
        public async Task<GameInfo> GetGameInfoByNameAsync(string gameName)
        {
            return await FetchAndCacheGameInfoAsync($"name_{gameName.ToLower()}", async () =>
            {
                await EnsureAccessTokenAsync();
                return await FetchGameFromApiByNameAsync(gameName);
            });
        }

        private async Task<GameInfo> FetchAndCacheGameInfoAsync(string cacheKey, Func<Task<GameInfo>> fetchFunc)
        {
            
            if (_cache.TryGetValue(cacheKey, out GameInfo cachedGame))
            {
                _logger.Debug($"Spiel '{cacheKey}' aus Memory-Cache geladen.");
                return cachedGame;
            }
            
            
            string cacheFilePath = Path.Combine(_localCacheDir, $"{cacheKey}.json");
            if (File.Exists(cacheFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(cacheFilePath);
                    var game = JsonSerializer.Deserialize<GameInfo>(json);
                    
                    if (game != null)
                    {
                        _logger.Debug($"Spiel '{cacheKey}' aus lokalem Cache geladen.");
                        _cache.Set(cacheKey, game, CacheDuration);
                        return game;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Fehler beim Laden des Spiels '{cacheKey}' aus lokalem Cache: {ex.Message}");
                }
            }
            
            
            try
            {
                if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                {
                    _logger.Warning("IGDB API-Zugangsdaten nicht konfiguriert. Verwende Fallback-Daten.");
                    return GetFallbackGameInfo(cacheKey);
                }
                
                var game = await fetchFunc();
                
                if (game != null)
                {
                    
                    _cache.Set(cacheKey, game, CacheDuration);
                    
                    
                    string json = JsonSerializer.Serialize(game, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(cacheFilePath, json);
                    
                    _logger.Info($"Spielinformationen für {game.Title} (ID: {game.Id}) erfolgreich abgerufen und gecacht.");
                    return game;
                }
                
                _logger.Warning($"Keine Spielinformationen für '{cacheKey}' gefunden. Verwende Fallback-Daten.");
                return GetFallbackGameInfo(cacheKey);
            }
            catch (Exception ex)
            {
                _logger.Error($"Fehler beim Abrufen der Spielinformationen für '{cacheKey}': {ex.Message}");
                return GetFallbackGameInfo(cacheKey);
            }
        }

        private async Task EnsureAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return; 
            }
            
            _logger.Debug("Hole neues Access-Token von Twitch...");
            
            var tokenEndpoint = "https://id.twitch.tv/oauth2/token";
            var tokenRequest = new Dictionary<string, string>
            {
                { "client_id", _clientId },
                { "client_secret", _clientSecret },
                { "grant_type", "client_credentials" }
            };
            
            var tokenContent = new FormUrlEncodedContent(tokenRequest);
            var tokenResponse = await _httpClient.PostAsync(tokenEndpoint, tokenContent);
            
            if (tokenResponse.IsSuccessStatusCode)
            {
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);
                
                _accessToken = tokenData.GetProperty("access_token").GetString();
                int expiresIn = tokenData.GetProperty("expires_in").GetInt32();
                
                
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                
                _logger.Debug("Neues Access-Token erhalten.");
            }
            else
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync();
                throw new Exception($"Fehler beim Abrufen des Access-Tokens: {tokenResponse.StatusCode} - {errorContent}");
            }
        }

        private async Task<GameInfo> FetchGameFromApiByIdAsync(int gameId)
        {
            _logger.Info($"Rufe Spielinformationen für ID {gameId} von IGDB ab...");
            string query = $"fields name,summary,genres.name,cover.url,screenshots.url,first_release_date,involved_companies.company.name,involved_companies.developer; where id = {gameId};";
            return await FetchGameFromApiWithQueryAsync(query);
        }

        private async Task<GameInfo> FetchGameFromApiByNameAsync(string gameName)
        {
            _logger.Info($"Rufe Spielinformationen für Name '{gameName}' von IGDB ab...");
            
            gameName = gameName.Replace("\"", "\\\"");
            string query = $"fields name,summary,genres.name,cover.url,screenshots.url,first_release_date,involved_companies.company.name,involved_companies.developer; search \"{gameName}\"; limit 1;";
            return await FetchGameFromApiWithQueryAsync(query);
        }

        private async Task<GameInfo> FetchGameFromApiWithQueryAsync(string query)
        {
            try
            {
                const string apiEndpoint = "https://api.igdb.com/v4/games";
                
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(apiEndpoint),
                    Content = new StringContent(query, Encoding.UTF8)
                };
                
                request.Headers.Add("Client-ID", _clientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    
                    if (string.IsNullOrWhiteSpace(content) || content == "[]")
                    {
                        _logger.Warning("Leere Ergebnisse von IGDB");
                        return null;
                    }
                    
                    var games = JsonSerializer.Deserialize<JsonElement[]>(content);
                    
                    if (games == null || games.Length == 0)
                    {
                        _logger.Warning("Keine Ergebnisse von IGDB");
                        return null;
                    }
                    
                    var gameData = games[0];
                    
                    
                    int igdbId = gameData.GetProperty("id").GetInt32();
                    
                    var game = new GameInfo
                    {
                        Id = igdbId,
                        Title = gameData.GetProperty("name").GetString()
                    };
                    
                    
                    if (gameData.TryGetProperty("summary", out var summaryElement))
                    {
                        game.Description = summaryElement.GetString();
                    }
                    
                    
                    if (gameData.TryGetProperty("cover", out var coverElement) && 
                        coverElement.TryGetProperty("url", out var coverUrlElement))
                    {
                        string coverUrl = coverUrlElement.GetString();
                        
                        coverUrl = coverUrl.Replace("//", "https://");
                        
                        game.ThumbnailUrl = coverUrl.Replace("t_thumb", "t_cover_small");
                        game.CoverUrl = coverUrl.Replace("t_thumb", "t_cover_big");
                    }
                    
                    
                    if (gameData.TryGetProperty("genres", out var genresElement) && genresElement.ValueKind == JsonValueKind.Array)
                    {
                        var genres = new List<string>();
                        foreach (var genreElement in genresElement.EnumerateArray())
                        {
                            if (genreElement.TryGetProperty("name", out var genreNameElement))
                            {
                                genres.Add(genreNameElement.GetString());
                            }
                        }
                        game.Genre = string.Join(", ", genres);
                    }
                    
                    
                    if (gameData.TryGetProperty("involved_companies", out var companiesElement) && 
                        companiesElement.ValueKind == JsonValueKind.Array)
                    {
                        var developers = new List<string>();
                        foreach (var companyElement in companiesElement.EnumerateArray())
                        {
                            if (companyElement.TryGetProperty("developer", out var isDeveloperElement) && 
                                isDeveloperElement.GetBoolean() && 
                                companyElement.TryGetProperty("company", out var company) &&
                                company.TryGetProperty("name", out var companyNameElement))
                            {
                                developers.Add(companyNameElement.GetString());
                            }
                        }
                        game.Developer = string.Join(", ", developers);
                    }
                    
                    
                    if (gameData.TryGetProperty("first_release_date", out var releaseDateElement))
                    {
                        
                        long timestamp = releaseDateElement.GetInt64();
                        game.ReleaseDate = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    }
                    
                    return game;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Error($"IGDB API-Fehler: {response.StatusCode} - {errorContent}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"API-Fehler: {ex.Message}");
                throw;
            }
        }

        private GameInfo GetFallbackGameInfo(string cacheKey)
        {
            
            if (int.TryParse(cacheKey, out int id))
            {
                
                switch (id)
                {
                    case 1020: 
                    case 20952: 
                        return new GameInfo
                        {
                            Id = 1020,
                            Title = "Grand Theft Auto V",
                            Description = "Grand Theft Auto V ist ein Open-World-Action-Adventure-Videospiel, das von Rockstar North entwickelt und von Rockstar Games veröffentlicht wurde.",
                            ThumbnailUrl = "https://images.igdb.com/igdb/image/upload/t_cover_small/co1tgl.jpg",
                            CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1tgl.jpg",
                            Genre = "Action, Adventure",
                            Developer = "Rockstar North",
                            ReleaseDate = new DateTime(2013, 9, 17),
                            Platform = "Multiple"
                        };
                    
                    case 1942: 
                    case 2282: 
                        return new GameInfo
                        {
                            Id = 1942,
                            Title = "The Witcher 3: Wild Hunt",
                            Description = "The Witcher 3: Wild Hunt ist ein Action-Rollenspiel, das von CD Projekt RED entwickelt wurde.",
                            ThumbnailUrl = "https://images.igdb.com/igdb/image/upload/t_cover_small/co1wyy.jpg",
                            CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1wyy.jpg",
                            Genre = "RPG",
                            Developer = "CD Projekt RED",
                            ReleaseDate = new DateTime(2015, 5, 19),
                            Platform = "Multiple"
                        };
                }
            }
            
            
            if (cacheKey.StartsWith("name_"))
            {
                string gameName = cacheKey.Substring(5); 
                return new GameInfo
                {
                    Id = 0,
                    Title = char.ToUpper(gameName[0]) + gameName.Substring(1), 
                    Description = "Keine Beschreibung verfügbar.",
                    ThumbnailUrl = "https://via.placeholder.com/150",
                    CoverUrl = "https://via.placeholder.com/300x450",
                    Genre = "Unbekannt",
                    Developer = "Unbekannt",
                    ReleaseDate = null,
                    Platform = "Unbekannt"
                };
            }
            
            
            return new GameInfo
            {
                Id = id,
                Title = $"Game {id}",
                Description = "Keine Beschreibung verfügbar.",
                ThumbnailUrl = "https://via.placeholder.com/150",
                CoverUrl = "https://via.placeholder.com/300x450",
                Genre = "Unbekannt",
                Developer = "Unbekannt",
                ReleaseDate = null,
                Platform = "Unbekannt"
            };
        }
    }
}