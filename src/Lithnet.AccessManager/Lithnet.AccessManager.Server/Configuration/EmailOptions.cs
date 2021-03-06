﻿using Newtonsoft.Json;

namespace Lithnet.AccessManager.Server.Configuration
{
    public class EmailOptions
    {
        [JsonIgnore]
        public bool IsConfigured => !string.IsNullOrWhiteSpace(this.Host);

        public string Host { get; set; }

        public int Port { get; set; } = 25;

        public bool UseSsl { get; set; }

        public bool UseDefaultCredentials { get; set; } = true;

        public string Username { get; set; }

        public EncryptedData Password { get; set; }

        public string FromAddress { get; set; }
    }
}