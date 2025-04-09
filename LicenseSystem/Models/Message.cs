using System;

namespace LicenseSystem.Models
{
    // Klasse für Nachrichten zwischen Server und Client (identisch zu der auf dem Server)
    public class Message
    {
        public string Type { get; set; } // "AUTH", "COMMAND", "NOTIFICATION", "DISCONNECT"
        public string Content { get; set; }
        public string Sender { get; set; }
        public DateTime Timestamp { get; set; }
    }
}