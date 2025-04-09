namespace LicenseSystem.Models
{
    public class LicenseKey
    {
        public string Key { get; set; }
        public DateTime GeneratedDate { get; set; }
        public DateTime ExpirationDate { get; set; }
        public bool IsUsed { get; set; }
        public string AssignedTo { get; set; }
        public int RateLimit { get; set; }
    }
}