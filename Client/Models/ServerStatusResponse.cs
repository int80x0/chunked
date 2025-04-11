namespace Client.Models
{
    public class ServerStatusResponse
    {
        public required ServerStatus ServerStatus { get; set; }
        public required ConnectionStatus ConnectionStatus { get; set; }
        public required SystemResources SystemResources { get; set; }
    }

    public abstract class ServerStatus
    {
        public required string Version { get; set; }
        public TimeSpan Uptime { get; set; }
        public int ConnectedUsers { get; set; }
        public int TotalUsers { get; set; }
        public int ActiveLicenses { get; set; }
        public int ExpiredLicenses { get; set; }
    }

    public abstract class ConnectionStatus
    {
        public bool IsConnected { get; set; }
        public int Ping { get; set; }
        public DateTime ConnectedSince { get; set; }
        public long DataSent { get; set; }
        public long DataReceived { get; set; }
    }

    public abstract class SystemResources
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public long AvailableDiskSpace { get; set; }
    }
}