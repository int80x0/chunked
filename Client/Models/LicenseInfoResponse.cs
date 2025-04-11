namespace Client.Models
{
    public class LicenseInfoResponse
    {
        public required LicenseInfo LicenseInfo { get; set; }
        public required ConnectionInfo ConnectionInfo { get; set; }
    }

    public abstract class LicenseInfo
    {
        public required string Username { get; set; }
        public required string LicenseKey { get; set; }
        public DateTime ExpirationDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime FirstLogin { get; set; }
        public DateTime LastLogin { get; set; }
        public required string IpAddress { get; set; }
        public required string ClientId { get; set; }
        public int RateLimit { get; set; }
    }

    public abstract class ConnectionInfo
    {
        public required string ServerAddress { get; set; }
        public DateTime ConnectedSince { get; set; }
        public int Ping { get; set; }
    }
}