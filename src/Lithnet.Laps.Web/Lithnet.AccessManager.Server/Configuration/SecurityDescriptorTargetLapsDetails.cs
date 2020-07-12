﻿using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithnet.AccessManager.Configuration
{
    public class SecurityDescriptorTargetLapsDetails
    {
        public TimeSpan ExpireAfter { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public PasswordStorageLocation RetrievalLocation { get; set; }
    }
}
