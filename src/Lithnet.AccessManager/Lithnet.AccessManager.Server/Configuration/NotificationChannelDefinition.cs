﻿using System;

namespace Lithnet.AccessManager.Server.Configuration
{
    public abstract class NotificationChannelDefinition
    {
        public bool Enabled { get; set; } = true;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public bool Mandatory { get; set; } = false;

        public string DisplayName { get; set; }
    }
}
