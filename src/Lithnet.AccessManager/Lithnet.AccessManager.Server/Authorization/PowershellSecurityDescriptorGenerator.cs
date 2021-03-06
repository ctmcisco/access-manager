﻿using System;
using System.IO;
using System.Management.Automation;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using Lithnet.AccessManager.Server.Configuration;
using Lithnet.AccessManager.Server.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Lithnet.AccessManager.Server.Authorization
{
    public class PowerShellSecurityDescriptorGenerator : IPowerShellSecurityDescriptorGenerator
    {
        private readonly ILogger logger;

        private readonly IPowerShellSessionProvider sessionProvider;

        public PowerShellSecurityDescriptorGenerator(ILogger<PowerShellSecurityDescriptorGenerator> logger, IPowerShellSessionProvider sessionProvider)
        {
            this.sessionProvider = sessionProvider;
            this.logger = logger;
        }

        public CommonSecurityDescriptor GenerateSecurityDescriptor(IUser user, IComputer computer, string script, int timeout)
        {
            PowerShellAuthorizationResponse result = this.GetAuthorizationResponse(script, user, computer, timeout);
            return GenerateSecurityDescriptor(user.Sid, result);
        }

        public CommonSecurityDescriptor GenerateSecurityDescriptor(SecurityIdentifier sid, PowerShellAuthorizationResponse result)
        {
            AccessMask allowedAccess = 0;
            AccessMask deniedAccess = 0;

            if (result.IsLocalAdminPasswordAllowed)
            {
                allowedAccess |= AccessMask.LocalAdminPassword;
            }

            if (result.IsLocalAdminPasswordHistoryAllowed)
            {
                allowedAccess |= AccessMask.LocalAdminPasswordHistory;
            }

            if (result.IsJitAllowed)
            {
                allowedAccess |= AccessMask.Jit;
            }

            if (result.IsLocalAdminPasswordDenied)
            {
                deniedAccess |= AccessMask.LocalAdminPassword;
            }

            if (result.IsLocalAdminPasswordHistoryDenied)
            {
                deniedAccess |= AccessMask.LocalAdminPasswordHistory;
            }

            if (result.IsJitDenied)
            {
                deniedAccess |= AccessMask.Jit;
            }

            DiscretionaryAcl dacl;

            if (allowedAccess > 0 && deniedAccess > 0)
            {
                dacl = new DiscretionaryAcl(false, false, 2);
            }
            else if (allowedAccess > 0 || deniedAccess > 0)
            {
                dacl = new DiscretionaryAcl(false, false, 1);
            }
            else
            {
                dacl = new DiscretionaryAcl(false, false, 0);
            }

            if (allowedAccess > 0)
            {
                dacl.AddAccess(AccessControlType.Allow, sid, (int)allowedAccess, InheritanceFlags.None, PropagationFlags.None);
            }

            if (deniedAccess > 0)
            {
                dacl.AddAccess(AccessControlType.Deny, sid, (int)deniedAccess, InheritanceFlags.None, PropagationFlags.None);
            }

            return new CommonSecurityDescriptor(false, false, ControlFlags.DiscretionaryAclPresent, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), null, null, dacl);
        }

        private PowerShellAuthorizationResponse GetAuthorizationResponse(string script, IUser user, IComputer computer, int timeout)
        {
            PowerShell powershell = this.sessionProvider.GetSession(script, "Get-AuthorizationResponse");
            powershell.AddCommand("Get-AuthorizationResponse")
                .AddParameter("user", this.ToPSObject(user))
                .AddParameter("computer", this.ToPSObject(computer));

            Task<PowerShellAuthorizationResponse> task = new Task<PowerShellAuthorizationResponse>(() =>
            {
                var results = powershell.Invoke();
                powershell.ThrowOnPipelineError();

                foreach (PSObject result in results)
                {
                    if (result.BaseObject is PowerShellAuthorizationResponse res)
                    {
                        return res;
                    }

                    if (result.Properties[nameof(res.IsLocalAdminPasswordAllowed)] == null &&
                        result.Properties[nameof(res.IsLocalAdminPasswordDenied)] == null &&
                        result.Properties[nameof(res.IsLocalAdminPasswordHistoryAllowed)] == null &&
                        result.Properties[nameof(res.IsLocalAdminPasswordHistoryDenied)] == null &&
                        result.Properties[nameof(res.IsJitAllowed)] == null &&
                        result.Properties[nameof(res.IsJitDenied)] == null)
                    {
                        continue;
                    }

                    res = new PowerShellAuthorizationResponse();
                    res.IsLocalAdminPasswordAllowed = Convert.ToBoolean(result.Properties[nameof(res.IsLocalAdminPasswordAllowed)]?.Value ?? false);
                    res.IsLocalAdminPasswordDenied = Convert.ToBoolean(result.Properties[nameof(res.IsLocalAdminPasswordDenied)]?.Value ?? false);
                    res.IsLocalAdminPasswordHistoryAllowed = Convert.ToBoolean(result.Properties[nameof(res.IsLocalAdminPasswordHistoryAllowed)]?.Value ?? false);
                    res.IsLocalAdminPasswordHistoryDenied = Convert.ToBoolean(result.Properties[nameof(res.IsLocalAdminPasswordHistoryDenied)]?.Value ?? false);
                    res.IsJitAllowed = Convert.ToBoolean(result.Properties[nameof(res.IsJitAllowed)]?.Value ?? false);
                    res.IsJitDenied = Convert.ToBoolean(result.Properties[nameof(res.IsJitDenied)]?.Value ?? false);
                    return res;
                }

                return null;
            });

            task.Start();
            if (!task.Wait(TimeSpan.FromSeconds(timeout)))
            {
                throw new TimeoutException("The PowerShell script did not complete within the configured time");
            }

            if (task.IsFaulted)
            {
                if (task.Exception != null) throw task.Exception;
                throw new AccessManagerException("The task failed");
            }

            if (task.Result != null)
            {
                this.logger.LogTrace($"PowerShell script returned the following AuthorizationResponse: {JsonConvert.SerializeObject(task.Result)}");
                return task.Result;
            }

            this.logger.LogWarning(EventIDs.PowerShellSDGeneratorInvalidResponse, $"The PowerShell script did not return an AuthorizationResponse");

            return new PowerShellAuthorizationResponse();
        }

        private PSObject ToPSObject(IUser user)
        {
            PSObject u = new PSObject();
            u.Properties.Add(new PSNoteProperty("Description", user.Description));
            u.Properties.Add(new PSNoteProperty("DisplayName", user.DisplayName));
            u.Properties.Add(new PSNoteProperty("DistinguishedName", user.DistinguishedName));
            u.Properties.Add(new PSNoteProperty("EmailAddress", user.EmailAddress));
            u.Properties.Add(new PSNoteProperty("GivenName", user.GivenName));
            u.Properties.Add(new PSNoteProperty("Guid", user.Guid));
            u.Properties.Add(new PSNoteProperty("MsDsPrincipalName", user.MsDsPrincipalName));
            u.Properties.Add(new PSNoteProperty("SamAccountName", user.SamAccountName));
            u.Properties.Add(new PSNoteProperty("Sid", user.Sid.ToString()));
            u.Properties.Add(new PSNoteProperty("Surname", user.Surname));
            u.Properties.Add(new PSNoteProperty("UserPrincipalName", user.UserPrincipalName));

            return u;
        }

        private PSObject ToPSObject(IComputer user)
        {
            PSObject u = new PSObject();
            u.Properties.Add(new PSNoteProperty("Description", user.Description));
            u.Properties.Add(new PSNoteProperty("DisplayName", user.DisplayName));
            u.Properties.Add(new PSNoteProperty("DistinguishedName", user.DistinguishedName));
            u.Properties.Add(new PSNoteProperty("Guid", user.Guid));
            u.Properties.Add(new PSNoteProperty("MsDsPrincipalName", user.MsDsPrincipalName));
            u.Properties.Add(new PSNoteProperty("SamAccountName", user.SamAccountName));
            u.Properties.Add(new PSNoteProperty("Sid", user.Sid.ToString()));

            return u;
        }
    }
}