namespace LicenseSystem.Models
{
    public class User
    {
        public string Username { get; set; }
        public string IP { get; set; }
        public DateTime FirstLogin { get; set; }
        public DateTime LastLogin { get; set; }
        public string LicenseKey { get; set; }
        public DateTime LicenseExpiration { get; set; }
        public int RateLimit { get; set; }
        public bool IsOnline { get; set; }
        public string ClientId { get; set; }
    }
}