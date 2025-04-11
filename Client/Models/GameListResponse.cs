namespace Client.Models
{
    public class GameListResponse
    {
        public required List<GameListItem> Games { get; set; }
        public int TotalCount { get; set; }
        public required string Category { get; set; }
    }

    public abstract class GameListItem
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public required string Description { get; set; }
        public long Size { get; set; }
        public required string Version { get; set; }
        public DateTime UploadDate { get; set; }
        public required string Developer { get; set; }
        public required string Genre { get; set; }
        public required string ThumbnailUrl { get; set; }
    }
}