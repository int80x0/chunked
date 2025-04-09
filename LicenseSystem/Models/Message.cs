using System;

namespace LicenseSystem.Models
{
    
    public class Message
    {
        public string Type { get; set; } 
        public string Content { get; set; }
        public string Sender { get; set; }
        public DateTime Timestamp { get; set; }
    }
}