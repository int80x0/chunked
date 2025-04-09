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
        
        // Zuordnungstabelle für gängige Spiele (alte ID -> IGDB ID)
        private static readonly Dictionary<int, int> LegacyIdToIgdbId = new Dictionary<int, int>
        {
            { 20952, 1020 },   // GTA V
            { 2282, 1942 },    // The Witcher 3
            { 1234, 472 },     // Fallout 4
            { 5678, 121 }      // Minecraft
            // Hier können weitere Mappings hinzugefügt werden
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

        /// <summary>
        /// Flexible Methode, die sowohl mit IDs als auch mit Spielnamen umgehen kann
        /// </summary>
        public async Task<GameInfo> GetGameInfoAsync(string nameOrId)
        {
            // Versuche, den Input als ID zu parsen
            if (int.TryParse(nameOrId, out int parsedId))
            {
                return await GetGameInfoByIdAsync(parsedId);
            }
            else
            {
                // Falls es keine ID ist, suche nach dem Namen
                return await GetGameInfoByNameAsync(nameOrId);
            }
        }

        /// <summary>
        /// Sucht ein Spiel anhand seiner ID (mit Legacy-ID-Unterstützung)
        /// </summary>
        public async Task<GameInfo> GetGameInfoByIdAsync(int gameId)
        {
            // Prüfe, ob es sich um eine alte ID handelt und konvertiere sie
            int igdbId = gameId;
            if (LegacyIdToIgdbId.TryGetValue(gameId, out int mappedId))
            {
                _logger.Debug($"Alte ID {gameId} zu IGDB ID {mappedId} konvertiert");
                igdbId = mappedId;
            }
            
            // Der restliche Prozess ist identisch, aber wir verwenden jetzt die IGDB ID
            return await FetchAndCacheGameInfoAsync(igdbId.ToString(), async () =>
            {
                await EnsureAccessTokenAsync();
                return await FetchGameFromApiByIdAsync(igdbId);
            });
        }

        /// <summary>
        /// Sucht ein Spiel anhand seines Namens
        /// </summary>
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
            // Versuche, aus dem Memory-Cache zu laden
            if (_cache.TryGetValue(cacheKey, out GameInfo cachedGame))
            {
                _logger.Debug($"Spiel '{cacheKey}' aus Memory-Cache geladen.");
                return cachedGame;
            }
            
            // Versuche, aus dem lokalen Datei-Cache zu laden
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
            
            // Wenn kein Cache verfügbar ist, rufe die API ab
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
                    // Speichere im Memory-Cache
                    _cache.Set(cacheKey, game, CacheDuration);
                    
                    // Speichere im lokalen Cache
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
                return; // Token ist noch gültig
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
                
                // Token läuft etwas früher ab, um Probleme zu vermeiden
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
            // Escape die Anführungszeichen im Namen
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
                    
                    // Überprüfen, ob es sich um ein valides JSON-Array handelt
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
                    
                    // Extrahiere die IGDB ID
                    int igdbId = gameData.GetProperty("id").GetInt32();
                    
                    var game = new GameInfo
                    {
                        Id = igdbId,
                        Title = gameData.GetProperty("name").GetString()
                    };
                    
                    // Beschreibung extrahieren
                    if (gameData.TryGetProperty("summary", out var summaryElement))
                    {
                        game.Description = summaryElement.GetString();
                    }
                    
                    // Cover-URL extrahieren
                    if (gameData.TryGetProperty("cover", out var coverElement) && 
                        coverElement.TryGetProperty("url", out var coverUrlElement))
                    {
                        string coverUrl = coverUrlElement.GetString();
                        // IGDB gibt URLs mit doppelten Schrägstrichen zurück - korrigiere das
                        coverUrl = coverUrl.Replace("//", "https://");
                        // Ändere die Größe von Thumbnail auf Full
                        game.ThumbnailUrl = coverUrl.Replace("t_thumb", "t_cover_small");
                        game.CoverUrl = coverUrl.Replace("t_thumb", "t_cover_big");
                    }
                    
                    // Genre extrahieren
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
                    
                    // Entwickler extrahieren
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
                    
                    // Veröffentlichungsdatum extrahieren
                    if (gameData.TryGetProperty("first_release_date", out var releaseDateElement))
                    {
                        // IGDB verwendet Unix-Zeitstempel
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
            // Versuche zu erkennen, ob es eine ID ist
            if (int.TryParse(cacheKey, out int id))
            {
                // Für bekannte Spiel-IDs können wir vordefinierte Daten zurückgeben
                switch (id)
                {
                    case 1020: // GTA V IGDB ID
                    case 20952: // Alte GTA V ID
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
                    
                    case 1942: // The Witcher 3 IGDB ID
                    case 2282: // Alte Witcher 3 ID
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
            
            // Für Namenssuchen versuchen wir, etwas sinnvolles zurückzugeben
            if (cacheKey.StartsWith("name_"))
            {
                string gameName = cacheKey.Substring(5); // "name_" entfernen
                return new GameInfo
                {
                    Id = 0,
                    Title = char.ToUpper(gameName[0]) + gameName.Substring(1), // Ersten Buchstaben groß machen
                    Description = "Keine Beschreibung verfügbar.",
                    ThumbnailUrl = "https://via.placeholder.com/150",
                    CoverUrl = "https://via.placeholder.com/300x450",
                    Genre = "Unbekannt",
                    Developer = "Unbekannt",
                    ReleaseDate = null,
                    Platform = "Unbekannt"
                };
            }
            
            // Generischer Fallback
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