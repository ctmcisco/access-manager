﻿using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Lithnet.AccessManager.Server.Auditing;
using Lithnet.AccessManager.Server.Authorization;
using Lithnet.AccessManager.Server.Extensions;
using Lithnet.AccessManager.Service.App_LocalResources;
using Lithnet.AccessManager.Service.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lithnet.AccessManager.Service.AppSettings
{
    public abstract class IdpAuthenticationProvider : HttpContextAuthenticationProvider, IIdpAuthenticationProvider
    {
        private readonly ILogger logger;

        private readonly IDirectory directory;

        protected abstract string ClaimName { get; }

        protected IdpAuthenticationProvider(ILogger logger, IDirectory directory, IHttpContextAccessor httpContextAccessor, IAuthorizationContextProvider authzContextProvider)
            : base(httpContextAccessor, directory, authzContextProvider)
        {
            this.logger = logger;
            this.directory = directory;
        }

        public Task HandleAuthNFailed(AccessDeniedContext context)
        {
            this.logger.LogEventError(EventIDs.ExternalAuthNAccessDenied, LogMessages.AuthNAccessDenied, context.Result?.Failure);
            context.HandleResponse();
            context.Response.Redirect($"/Home/AuthNError?messageid={(int)AuthNFailureMessageID.ExternalAuthNProviderDenied}");

            return Task.CompletedTask;
        }

        public Task HandleRemoteFailure(RemoteFailureContext context)
        {
            this.logger.LogEventError(EventIDs.ExternalAuthNProviderError, LogMessages.AuthNProviderError, context.Failure);
            context.HandleResponse();
            context.Response.Redirect($"/Home/AuthNError?messageid={(int)AuthNFailureMessageID.ExternalAuthNProviderError}");

            return Task.CompletedTask;
        }

        public Task FindClaimIdentityInDirectoryOrFail<T>(RemoteAuthenticationContext<T> context) where T : AuthenticationSchemeOptions
        {
            try
            {
                ClaimsIdentity user = context.Principal.Identity as ClaimsIdentity;
                var directoryUser = this.FindUserByClaim(user, this.ClaimName);
                string sid = directoryUser?.Sid?.Value;

                if (sid == null)
                {
                    string message = string.Format(LogMessages.UserNotFoundInDirectory, user.ToClaimList());
                    this.logger.LogEventError(EventIDs.SsoIdentityNotFound, message, null);
                    context.HandleResponse();
                    context.Response.Redirect($"/Home/AuthNError?messageid={(int)AuthNFailureMessageID.SsoIdentityNotFound}");
                    return Task.CompletedTask;
                }

                user.AddClaim(new Claim(ClaimTypes.PrimarySid, sid));
                this.AddAuthZClaims(directoryUser, user);
                this.logger.LogEventSuccess(EventIDs.UserAuthenticated, string.Format(LogMessages.AuthenticatedAndMappedUser, user.ToClaimList()));
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                this.logger.LogEventError(EventIDs.AuthNResponseProcessingError, LogMessages.AuthNResponseProcessingError, ex);
                context.HandleResponse();
                context.Response.Redirect($"/Home/AuthNError?messageid={(int)AuthNFailureMessageID.SsoIdentityNotFound}");
                return Task.CompletedTask;
            }
        }

        private IUser FindUserByClaim(ClaimsIdentity p, string claimName)
        {
            Claim c = p.FindFirst(claimName);

            if (c != null)
            {
                this.logger.LogTrace($"Attempting to find a match in the directory for externally provided claim {c.Type}:{c.Value}");

                try
                {
                    return this.directory.GetUser(c.Value);
                }
                catch (Exception ex)
                {
                    this.logger.LogEventError(EventIDs.AuthNDirectoryLookupError, string.Format(LogMessages.AuthNDirectoryLookupError, c.Type, c.Value), ex);
                }
            }

            return null;
        }
    }
}