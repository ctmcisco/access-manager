﻿using System;
using Lithnet.AccessManager.Server.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lithnet.AccessManager.Web.Internal
{
    public sealed class RateLimiter : IRateLimiter
    {
        private readonly RateLimitOptions rateLimits;
        private readonly IMemoryCache memoryCache;

        public RateLimiter(IOptions<RateLimitOptions> rateLimits, IMemoryCache memoryCache)
        {
            this.rateLimits = rateLimits.Value;
            this.memoryCache = memoryCache;
        }

        public RateLimitResult GetRateLimitResult(string userid, HttpRequest r)
        {
            if (this.rateLimits.PerIP.Enabled)
            {
                RateLimitResult result =
                    this.IsIpThresholdExceeded(r, this.rateLimits.PerIP.RequestsPerMinute, 60) ??
                    this.IsIpThresholdExceeded(r, this.rateLimits.PerIP.RequestsPerHour, 3600) ??
                    this.IsIpThresholdExceeded(r, this.rateLimits.PerIP.RequestsPerDay, 86400);

                if (result != null)
                {
                    result.UserID = userid;
                    result.IPAddress = r.HttpContext.Connection.RemoteIpAddress.ToString();
                    return result;
                }
            }

            if (this.rateLimits.PerUser.Enabled)
            {
                RateLimitResult result =
                    this.IsUserThresholdExceeded(userid, this.rateLimits.PerUser.RequestsPerMinute, 60) ??
                    this.IsUserThresholdExceeded(userid, this.rateLimits.PerUser.RequestsPerHour, 3600) ??
                    this.IsUserThresholdExceeded(userid, this.rateLimits.PerUser.RequestsPerDay, 86400);

                if (result != null)
                {
                    result.UserID = userid;
                    result.IPAddress = r.HttpContext.Connection.RemoteIpAddress.ToString();
                    return result;
                }
            }

            return new RateLimitResult() { IsRateLimitExceeded = false };
        }

        private RateLimitResult IsIpThresholdExceeded(HttpRequest r, int threshold, int duration)
        {
            string ip = r.HttpContext.Connection.RemoteIpAddress.ToString();

            if (this.IsThresholdExceeded(ip, threshold, duration))
            {
                return new RateLimitResult { Duration = duration, IPAddress = ip, IsRateLimitExceeded = true, IsUserRateLimit = false, Threshold = threshold };
            }

            return null;
        }

        private RateLimitResult IsUserThresholdExceeded(string userid, int threshold, int duration)
        {
            if (this.IsThresholdExceeded(userid, threshold, duration))
            {
                return new RateLimitResult { Duration = duration, IsRateLimitExceeded = true, IsUserRateLimit = true, Threshold = threshold };
            }

            return null;
        }

        private bool IsThresholdExceeded(string usernameOrIP, int threshold, int duration)
        {
            string key1 = string.Join(@"-", duration, threshold, usernameOrIP);
            return this.IsThresholdExceededForKey(key1, threshold, duration);
        }

        private bool IsThresholdExceededForKey(string key, int threshold, int duration)
        {
            if (threshold <= 0)
            {
                return false;
            }

            if (!this.memoryCache.TryGetValue<int>(key, out int count))
            {
                count = 1;
            }
            else
            {
                count++;
            }

            this.memoryCache.Set<int>(
                key,
                count,
                DateTime.UtcNow.AddSeconds(duration)
            );

            return count > threshold;
        }
    }
}