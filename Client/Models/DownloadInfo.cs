namespace Client.Models
{
    public abstract class DownloadInfo
    {
        public int GameId { get; init; }
        public required string GameTitle { get; init; }
        public required string FileId { get; init; }
        public int ChunkCount { get; init; }
        public abstract required List<ChunkInfo> Chunks { get; init; }
        public long TotalSize { get; init; }
    }

    public abstract class ChunkInfo
    {
        public int Index { get; set; }
        public required string Id { get; set; }
        public long Size { get; set; }
        public required string Url { get; set; }
        public required string Hash { get; set; }
    }
}