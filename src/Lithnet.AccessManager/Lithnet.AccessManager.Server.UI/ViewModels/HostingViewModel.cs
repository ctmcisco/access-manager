﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.AccessManager.Server.Configuration;
using Lithnet.AccessManager.Server.UI.Interop;
using MahApps.Metro.Controls.Dialogs;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using NetFwTypeLib;
using Newtonsoft.Json;
using SslCertBinding.Net;
using Stylet;

namespace Lithnet.AccessManager.Server.UI
{
    public sealed class HostingViewModel : Screen, IHelpLink
    {
        private const string SddlTemplate = "D:(A;;GX;;;{0})";

        private CancellationTokenSource cancellationTokenSource;

        private readonly IDialogCoordinator dialogCoordinator;

        private readonly ILogger<HostingViewModel> logger;

        private readonly IAppPathProvider pathProvider;

        private readonly IServiceSettingsProvider serviceSettings;

        private readonly ICertificateProvider certProvider;

        public HostingViewModel(HostingOptions model, IDialogCoordinator dialogCoordinator, IServiceSettingsProvider serviceSettings, ILogger<HostingViewModel> logger, IModelValidator<HostingViewModel> validator, IAppPathProvider pathProvider, INotifiableEventPublisher eventPublisher, ICertificateProvider certProvider)
        {
            this.logger = logger;
            this.pathProvider = pathProvider;
            this.OriginalModel = model;
            this.certProvider = certProvider;
            this.dialogCoordinator = dialogCoordinator;
            this.serviceSettings = serviceSettings;
            this.Validator = validator;

            this.WorkingModel = this.CloneModel(model);
            this.Certificate = this.GetCertificate();
            this.OriginalCertificate = this.Certificate;
            this.ServiceAccount = this.serviceSettings.GetServiceAccount();
            this.OriginalServiceAccount = this.ServiceAccount;
            this.ServiceStatus = this.serviceSettings.ServiceController.Status.ToString();
            this.DisplayName = "Web hosting";

            eventPublisher.Register(this);
        }

        public string HelpLink => Constants.HelpLinkPageWebHosting;

        protected override void OnInitialActivate()
        {
            _ = this.TryGetVersion();
            this.PopulateCanDelegate();
            this.PopulateIsNotGmsa();
        }

        protected override void OnActivate()
        {
            Debug.WriteLine("Poll activate");

            this.cancellationTokenSource = new CancellationTokenSource();
            _ = this.PollServiceStatus(this.cancellationTokenSource.Token);
            base.OnActivate();
        }

        protected override void OnDeactivate()
        {
            Debug.WriteLine("Poll stopping");
            this.cancellationTokenSource.Cancel();

            base.OnDeactivate();
        }

        public string AvailableVersion { get; set; }

        public bool CanShowCertificateDialog => this.Certificate != null;

        public bool CanStartService => this.ServiceStatus == ServiceControllerStatus.Stopped.ToString();

        public bool CanStopService => this.ServiceStatus == ServiceControllerStatus.Running.ToString();

        [NotifiableProperty]
        public X509Certificate2 Certificate { get; set; }

        public string CertificateDisplayName => this.Certificate.ToDisplayName();

        public string CertificateExpiryText { get; set; }

        public string CurrentVersion { get; set; }

        [NotifiableProperty]
        public string Hostname { get => this.WorkingModel.HttpSys.Hostname; set => this.WorkingModel.HttpSys.Hostname = value; }

        [NotifiableProperty]
        public int HttpPort { get => this.WorkingModel.HttpSys.HttpPort; set => this.WorkingModel.HttpSys.HttpPort = value; }

        [NotifiableProperty]
        public int HttpsPort { get => this.WorkingModel.HttpSys.HttpsPort; set => this.WorkingModel.HttpSys.HttpsPort = value; }

        public PackIconMaterialKind Icon => PackIconMaterialKind.Web;

        public bool IsCertificateCurrent { get; set; }

        public bool IsCertificateExpired { get; set; }

        public bool IsCertificateExpiring { get; set; }

        public bool IsUpToDate { get; set; }

        [NotifiableProperty]
        public SecurityIdentifier ServiceAccount { get; set; }

        public string ServiceAccountDisplayName
        {
            get
            {
                try
                {
                    return this.ServiceAccount?.Translate(typeof(NTAccount))?.Value ?? "<not set>";
                }
                catch
                {
                    return this.ServiceAccount?.ToString() ?? "<not set>";
                }
            }
        }

        public bool ServicePending { get; set; }

        public string ServiceStatus { get; set; }

        public bool CanBeDelegated { get; set; }

        public bool IsNotGmsa { get; set; }

        private void PopulateCanDelegate()
        {
            try
            {
                this.CanBeDelegated = false;

                using PrincipalContext ctx = new PrincipalContext(ContextType.Domain);
                using var u = UserPrincipal.FindByIdentity(ctx, IdentityType.Sid, this.ServiceAccount.ToString());

                if (u != null)
                {
                    this.CanBeDelegated = u.DelegationPermitted;
                    return;
                }

                using var cmp = ComputerPrincipal.FindByIdentity(ctx, IdentityType.Sid, this.ServiceAccount.ToString());

                if (cmp != null)
                {
                    this.CanBeDelegated = cmp.DelegationPermitted;
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIGenericError, ex, "Could not determine delegation status of account");
            }
        }

        private void PopulateIsNotGmsa()
        {
            try
            {
                this.IsNotGmsa = false;

                using PrincipalContext ctx = new PrincipalContext(ContextType.Domain);
                using var cmp = ComputerPrincipal.FindByIdentity(ctx, IdentityType.Sid, this.ServiceAccount.ToString());

                if (cmp != null)
                {
                    this.IsNotGmsa = !string.Equals(cmp.StructuralObjectClass, "msDS-GroupManagedServiceAccount", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIGenericError, ex, "Could not determine gmsa status of account");
            }
        }



        public async Task OpenGmsaInfo()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Constants.LinkGmsaInfo,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                logger.LogWarning(EventIDs.UIGenericWarning, ex, "Could not open link");
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"Could not open the default link handler\r\n{ex.Message}");
            }
        }

        public void PreventDelegation()
        {
            var vm = new ScriptContentViewModel(this.dialogCoordinator)
            {
                HelpText = "Run the following script as an account that is a member of the 'Domain admins' group",
                ScriptText = ScriptTemplates.PreventDelegation
                    .Replace("{sid}", this.ServiceAccount.ToString(), StringComparison.OrdinalIgnoreCase)
            };

            ExternalDialogWindow w = new ExternalDialogWindow
            {
                Title = "Script",
                DataContext = vm,
                SaveButtonVisible = false,
                CancelButtonName = "Close"
            };

            w.ShowDialog();

            this.PopulateCanDelegate();
        }

        public void CreateGmsa()
        {
            var vm = new ScriptContentViewModel(this.dialogCoordinator)
            {
                HelpText = "Run the following script as an account that is a member of the 'Domain admins' group",
                ScriptText = ScriptTemplates.CreateGmsa
                    .Replace("{serverName}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            };

            ExternalDialogWindow w = new ExternalDialogWindow
            {
                Title = "Script",
                DataContext = vm,
                SaveButtonVisible = false,
                CancelButtonName = "Close"
            };

            w.ShowDialog();
        }

        public bool ShowCertificateExpiryWarning => this.Certificate != null && this.Certificate.NotAfter.AddDays(-30) >= DateTime.Now;

        public bool UpdateAvailable { get; set; }

        public string UpdateLink { get; set; }

        private X509Certificate2 OriginalCertificate { get; set; }

        private HostingOptions OriginalModel { get; set; }

        private SecurityIdentifier OriginalServiceAccount { get; set; }

        private HostingOptions WorkingModel { get; set; }

        private string workingServiceAccountPassword { get; set; }

        private string workingServiceAccountUserName { get; set; }

        public async Task<bool> CommitSettings()
        {
            if (this.Certificate == null)
            {
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", "You must select a HTTPS certificate");
                return false;
            }

            RegistryKey key = Registry.LocalMachine.OpenSubKey(AccessManager.Constants.BaseKey, false);

            bool currentlyUnconfigured = !(key?.GetValue("Configured", 0) is int value) || value == 0;

            bool updatePrivateKeyPermissions =
                this.ServiceAccount != this.OriginalServiceAccount ||
                this.Certificate?.Thumbprint != this.OriginalCertificate?.Thumbprint ||
                currentlyUnconfigured;

            bool updateHttpReservations =
                this.WorkingModel.HttpSys.Hostname != this.OriginalModel.HttpSys.Hostname ||
                this.WorkingModel.HttpSys.HttpPort != this.OriginalModel.HttpSys.HttpPort ||
                this.WorkingModel.HttpSys.HttpsPort != this.OriginalModel.HttpSys.HttpsPort ||
                this.ServiceAccount != this.OriginalServiceAccount ||
                currentlyUnconfigured;

            bool updateConfigFile = updateHttpReservations;

            bool updateFirewallRules = updateHttpReservations;

            bool updateCertificateBinding =
                this.WorkingModel.HttpSys.HttpsPort != this.OriginalModel.HttpSys.HttpsPort ||
                this.Certificate?.Thumbprint != this.OriginalCertificate?.Thumbprint ||
                currentlyUnconfigured;

            bool updateServiceAccount = this.workingServiceAccountUserName != null;

            HostingSettingsRollbackContext rollbackContext = new HostingSettingsRollbackContext();
            rollbackContext.StartingUnconfigured = currentlyUnconfigured;

            try
            {
                if (updatePrivateKeyPermissions)
                {
                    this.UpdateCertificatePermissions(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Could not add private key permissions for SSL certificate");
                var result = await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"An error occurred while trying to add permissions for the service account {this.ServiceAccountDisplayName} to read the private key of the specified certificate. Try adding permissions for this manually using the Windows computer certificates MMC console. Do you want to continue with the operation?\r\n{ex.Message}", MessageDialogStyle.AffirmativeAndNegative);

                if (result == MessageDialogResult.Canceled)
                {
                    return false;
                }
            }

            try
            {
                if (updatePrivateKeyPermissions)
                {
                    this.UpdateEncryptionCertificateAcls(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Could not add private key permissions for encryption certificate");
                var result = await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"An error occurred while trying to add permissions for the service account {this.ServiceAccountDisplayName} to read the private key of one of the existing password encryption certificates. Try adding permissions for this manually using the Windows service certificates MMC console. Do you want to continue with the operation?\r\n{ex.Message}", MessageDialogStyle.AffirmativeAndNegative);

                if (result == MessageDialogResult.Canceled)
                {
                    return false;
                }
            }

            try
            {
                if (updateHttpReservations)
                {
                    string httpOld = this.OriginalModel.HttpSys.BuildHttpUrlPrefix();
                    string httpsOld = this.OriginalModel.HttpSys.BuildHttpsUrlPrefix();
                    string httpNew = this.WorkingModel.HttpSys.BuildHttpUrlPrefix();
                    string httpsNew = this.WorkingModel.HttpSys.BuildHttpsUrlPrefix();

                    if (this.IsReservationInUse(currentlyUnconfigured, httpOld, httpNew, out string user))
                    {
                        var result = await this.dialogCoordinator.ShowMessageAsync(this, "Warning", $"The HTTP URL '{this.WorkingModel.HttpSys.BuildHttpUrlPrefix()}' is already registered to user {user}. Do you want to overwrite it?", MessageDialogStyle.AffirmativeAndNegative);

                        if (result == MessageDialogResult.Negative)
                        {
                            return false;
                        }
                    }

                    if (this.IsReservationInUse(currentlyUnconfigured, httpsOld, httpsNew, out user))
                    {
                        var result = await this.dialogCoordinator.ShowMessageAsync(this, "Warning", $"The HTTPS URL '{this.WorkingModel.HttpSys.BuildHttpsUrlPrefix()}' is already registered to user {user}. Do you want to overwrite it?", MessageDialogStyle.AffirmativeAndNegative);

                        if (result == MessageDialogResult.Negative)
                        {
                            return false;
                        }
                    }

                    this.CreateNewHttpReservations(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Error creating HTTP reservations");
                rollbackContext.Rollback(this.logger);
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"Could not create the HTTP reservations\r\n{ex.Message}");
                return false;
            }

            try
            {
                if (updateFirewallRules)
                {
                    this.ReplaceFirewallRules(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Error updating the firewall rules");
                rollbackContext.Rollback(this.logger);

                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"Could not update the firewall rules. Please manually update them to ensure your users can access the application\r\n{ex.Message}");
                return false;
            }

            try
            {
                if (updateConfigFile)
                {
                    this.SaveHostingConfigFile(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Could not save updated config file");
                rollbackContext.Rollback(this.logger);
                return false;
            }

            try
            {
                if (updateCertificateBinding)
                {
                    this.UpdateCertificateBinding(rollbackContext);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex, "Error creating certificate binding");
                rollbackContext.Rollback(this.logger);
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"Could not bind the certificate to the specified port\r\n{ex.Message}");

                return false;
            }

            try
            {
                if (updateServiceAccount)
                {
                    this.UpdateServiceAccount();
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(EventIDs.UIConfigurationSaveError, ex,
                    "Could not change the service account to the specified account {serviceAccountName}",
                    workingServiceAccountUserName);
                rollbackContext.Rollback(this.logger);
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"The service account could not be changed\r\n{ex.Message}");
                return false;
            }

            if (updateCertificateBinding || updateHttpReservations || updatePrivateKeyPermissions ||
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                updateServiceAccount || updateConfigFile || updateFirewallRules)
            {
                this.OriginalModel = this.CloneModel(this.WorkingModel);
                this.OriginalCertificate = this.Certificate;
                this.OriginalServiceAccount = this.ServiceAccount;

                await this.dialogCoordinator.ShowMessageAsync(this, "Configuration updated", $"The service configuration has been updated. Restart the service for the new settings to take effect");
            }

            return true;
        }

        private void UpdateServiceAccount()
        {
            this.serviceSettings.SetServiceAccount(this.workingServiceAccountUserName, this.workingServiceAccountPassword);
        }

        private void SaveHostingConfigFile(HostingSettingsRollbackContext rollback)
        {
            var originalSettings = this.OriginalModel;

            this.WorkingModel.Save(pathProvider.HostingConfigFile);
            rollback.RollbackActions.Add(() => originalSettings.Save(pathProvider.HostingConfigFile));
        }

        private void UpdateCertificatePermissions(HostingSettingsRollbackContext rollback)
        {
            if (this.Certificate == null)
            {
                return;
            }

            var originalCertificateSecurity = this.Certificate.GetPrivateKeySecurity();
            var certificate = this.Certificate;

            certificate.AddPrivateKeyReadPermission(this.ServiceAccount);
            rollback.RollbackActions.Add(() => certificate.SetPrivateKeySecurity(originalCertificateSecurity));
        }

        public async Task DownloadUpdate()
        {
            try
            {
                if (this.UpdateLink == null)
                {
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = this.UpdateLink,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                logger.LogWarning(EventIDs.UIGenericWarning, ex, "Could not open editor");
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"Could not start default browser\r\n{ex.Message}");
            }
        }

        public void OnCertificateChanged()
        {
            this.IsCertificateCurrent = false;
            this.IsCertificateExpired = false;
            this.IsCertificateExpiring = false;

            if (this.Certificate == null)
            {
                this.CertificateExpiryText = "Select a certificate";
                return;
            }

            TimeSpan remainingTime = this.Certificate.NotAfter.Subtract(DateTime.Now);

            if (remainingTime.Ticks <= 0)
            {
                this.IsCertificateExpired = true;
                this.CertificateExpiryText = "The certificate has expired";
                return;
            }

            if (remainingTime.TotalDays < 30)
            {
                this.IsCertificateExpiring = true;
            }
            else
            {
                this.IsCertificateCurrent = true;
            }

            this.CertificateExpiryText = $"Certificate expires in {remainingTime:%d} days";
        }

        public async Task RestartService()
        {
            await this.StopService();
            await this.StartService();
        }

        public async Task SelectServiceAccountUser()
        {
            var r = await this.dialogCoordinator.ShowLoginAsync(this, "Service account", "Enter the credentials for the service account", new LoginDialogSettings
            {
                EnablePasswordPreview = true,
                AffirmativeButtonText = "OK"
            });

            if (r == null)
            {
                return;
            }

            try
            {
                ActiveDirectory directory = new ActiveDirectory();
                if (directory.TryGetPrincipal(r.Username, out ISecurityPrincipal o))
                {
                    if (o is IGroup)
                    {
                        throw new DirectoryException("The selected object must be a user");
                    }

                    this.ServiceAccount = o.Sid;
                }
                else
                {
                    using PrincipalContext p = new PrincipalContext(ContextType.Machine);
                    var up = UserPrincipal.FindByIdentity(p, r.Username);

                    if (up == null)
                    {
                        throw new ObjectNotFoundException("The user could not be found");
                    }

                    this.ServiceAccount = up.Sid;
                }

                this.workingServiceAccountUserName = r.Username;
                this.workingServiceAccountPassword = r.Password;

                this.PopulateIsNotGmsa();
            }
            catch (Exception ex)
            {
                await this.dialogCoordinator.ShowMessageAsync(this, "Error", $"The credentials provided could not be validated\r\n{ex.Message}");
            }
        }

        public void ShowCertificateDialog()
        {
            X509Certificate2UI.DisplayCertificate(this.Certificate, this.GetHandle());
        }

        public void ShowImportDialog()
        {
            X509Certificate2 newCert = NativeMethods.ShowCertificateImportDialog(this.GetHandle(), "Import certificate", StoreLocation.LocalMachine, StoreName.My);

            if (newCert != null)
            {
                this.Certificate = newCert;
            }
        }

        public void ShowSelectCertificateDialog()
        {
            X509Certificate2Collection results = X509Certificate2UI.SelectFromCollection(this.GetAvailableCertificateCollection(), "Select TLS certificate", "Select a certificate to use as the TLS certificate for this web site", X509SelectionFlag.SingleSelection, this.GetHandle());

            if (results.Count == 1)
            {
                this.Certificate = results[0];
            }
        }

        public async Task StartService()
        {
            try
            {
                if (this.CanStartService)
                {
                    this.serviceSettings.ServiceController.Start();
                }
            }
            catch (Exception ex)
            {
                await dialogCoordinator.ShowMessageAsync(this, "Service control", $"Could not start service\r\n{ex.Message}");
                return;
            }

            try
            {
                await this.serviceSettings.ServiceController.WaitForStatusAsync(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                await dialogCoordinator.ShowMessageAsync(this, "Service control", "The service did not start in the requested time");
            }
        }

        public async Task StopService()
        {
            try
            {
                if (this.CanStopService)
                {
                    this.serviceSettings.ServiceController.Stop();
                }
            }
            catch (Exception ex)
            {
                await dialogCoordinator.ShowMessageAsync(this, "Service control", $"Could not stop service\r\n{ex.Message}");
                return;
            }
            try
            {
                await this.serviceSettings.ServiceController.WaitForStatusAsync(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                await dialogCoordinator.ShowMessageAsync(this, "Service control", "The service did not stop in the requested time");
            }
        }

        public async Task TryGetVersion()
        {
            this.UpdateAvailable = false;
            this.IsUpToDate = false;
            this.UpdateLink = null;
            this.AvailableVersion = null;

            try
            {
                var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
                this.CurrentVersion = currentVersion?.ToString() ?? "Could not determine version";

                string appdata = await DownloadFile(Constants.UrlProductVersionInfo);
                if (appdata != null)
                {
                    var versionInfo = JsonConvert.DeserializeObject<PublishedVersionInfo>(appdata);

                    if (Version.TryParse(versionInfo.CurrentVersion, out Version onlineVersion))
                    {
                        this.AvailableVersion = onlineVersion.ToString();

                        if (onlineVersion > currentVersion)
                        {
                            this.UpdateAvailable = true;
                            this.IsUpToDate = false;
                            this.UpdateLink = versionInfo.UserUrl;
                        }
                        else
                        {
                            this.UpdateAvailable = false;
                            this.IsUpToDate = true;
                            this.UpdateLink = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(EventIDs.UIGenericWarning, ex, "Could not get version update");
                this.AvailableVersion = "Unable to determine latest application version";
            }
        }

        private static async Task<string> DownloadFile(string url)
        {
            using var client = new HttpClient();
            using var result = await client.GetAsync(url);

            if (result.IsSuccessStatusCode)
            {
                return await result.Content.ReadAsStringAsync();
            }

            return null;
        }

        private T CloneModel<T>(T model)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(model));
        }

        private bool IsReservationInUse(bool currentlyUnconfigured, string oldurl, string newurl, out string user)
        {
            user = null;

            if (!currentlyUnconfigured && oldurl == newurl)
            {
                return false;
            }

            var acl = this.GetUrlReservation(newurl);

            if (acl == null)
            {
                return false;
            }

            SecurityIdentifier currentOwner = null;

            CommonSecurityDescriptor sd = new CommonSecurityDescriptor(false, false, acl.Sddl);
            foreach (var dacl in sd.DiscretionaryAcl.OfType<CommonAce>())
            {
                if (dacl.SecurityIdentifier == this.ServiceAccount ||
                    dacl.SecurityIdentifier == this.OriginalServiceAccount)
                {
                    return false;
                }

                currentOwner = dacl.SecurityIdentifier;
            }

            if (currentOwner == null)
            {
                return false;
            }

            try
            {
                user = ((NTAccount)currentOwner.Translate(typeof(NTAccount))).Value;
            }
            catch
            {
                user = currentOwner.ToString();
            }

            return true;
        }


        private void CreateNewHttpReservations(HostingSettingsRollbackContext rollback)
        {
            if (this.ServiceAccount == null)
            {
                return;
            }

            string httpOld = this.OriginalModel.HttpSys.BuildHttpUrlPrefix();
            string httpsOld = this.OriginalModel.HttpSys.BuildHttpsUrlPrefix();
            string httpNew = this.WorkingModel.HttpSys.BuildHttpUrlPrefix();
            string httpsNew = this.WorkingModel.HttpSys.BuildHttpsUrlPrefix();

            var existingHttpOld = this.GetUrlReservation(httpOld);
            if (existingHttpOld != null)
            {
                UrlAcl.Delete(existingHttpOld.Prefix);
                rollback.RollbackActions.Add(() => UrlAcl.Create(existingHttpOld.Prefix, existingHttpOld.Sddl));
            }

            var existingHttpsOld = this.GetUrlReservation(httpsOld);
            if (existingHttpsOld != null)
            {
                UrlAcl.Delete(existingHttpsOld.Prefix);
                rollback.RollbackActions.Add(() => UrlAcl.Create(existingHttpsOld.Prefix, existingHttpsOld.Sddl));
            }

            var existingHttpNew = this.GetUrlReservation(httpNew);
            if (existingHttpNew != null)
            {
                UrlAcl.Delete(existingHttpNew.Prefix);
                rollback.RollbackActions.Add(() => UrlAcl.Create(existingHttpNew.Prefix, existingHttpNew.Sddl));
            }

            var existingHttpsNew = this.GetUrlReservation(httpsNew);
            if (existingHttpsNew != null)
            {
                UrlAcl.Delete(existingHttpsNew.Prefix);
                rollback.RollbackActions.Add(() => UrlAcl.Create(existingHttpsNew.Prefix, existingHttpsNew.Sddl));
            }

            this.CreateUrlReservation(httpNew, this.ServiceAccount);
            rollback.RollbackActions.Add(() => UrlAcl.Delete(httpNew));

            this.CreateUrlReservation(httpsNew, this.ServiceAccount);
            rollback.RollbackActions.Add(() => UrlAcl.Delete(httpsNew));
        }

        private void CreateUrlReservation(string url, SecurityIdentifier sid)
        {
            UrlAcl.Create(url, string.Format(SddlTemplate, sid));
        }

        private void DeleteUrlReservation(string url)
        {
            foreach (var acl in UrlAcl.GetAllBindings())
            {
                if (string.Equals(acl.Prefix, url, StringComparison.OrdinalIgnoreCase))
                {
                    UrlAcl.Delete(acl.Prefix);
                }
            }
        }

        private UrlAcl GetUrlReservation(string url)
        {
            foreach (var acl in UrlAcl.GetAllBindings())
            {
                if (string.Equals(acl.Prefix, url, StringComparison.OrdinalIgnoreCase))
                {
                    return acl;
                }
            }

            return null;
        }

        private void UpdateEncryptionCertificateAcls(HostingSettingsRollbackContext rollback)
        {
            foreach (var cert in this.certProvider.GetEligibleCertificates(true))
            {
                var originalCertificateSecurity = cert.GetPrivateKeySecurity();

                cert.AddPrivateKeyReadPermission(this.ServiceAccount);
                rollback.RollbackActions.Add(() => cert.SetPrivateKeySecurity(originalCertificateSecurity));
            }
        }

        private X509Certificate2Collection GetAvailableCertificateCollection()
        {
            X509Certificate2Collection certs = new X509Certificate2Collection();

            X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine, OpenFlags.ReadOnly);
            Oid serverAuthOid = new Oid("1.3.6.1.5.5.7.3.1");

            foreach (X509Certificate2 c in store.Certificates.Find(X509FindType.FindByTimeValid, DateTime.Now, false).OfType<X509Certificate2>().Where(t => t.HasPrivateKey))
            {
                foreach (X509EnhancedKeyUsageExtension x in c.Extensions.OfType<X509EnhancedKeyUsageExtension>())
                {
                    foreach (Oid o in x.EnhancedKeyUsages)
                    {
                        if (o.Value == serverAuthOid.Value)
                        {
                            certs.Add(c);
                        }
                    }
                }
            }

            return certs;
        }

        private X509Certificate2 GetCertificate()
        {
            foreach (CertificateBinding binding in this.GetCertificateBindings())
            {
                if (binding.AppId == HttpSysHostingOptions.AppId)
                {
                    return this.GetCertificateFromStore(binding.StoreName, binding.Thumbprint);
                }
            }

            return null;
        }

        private List<CertificateBinding> GetCertificateBindings()
        {
            var config = new CertificateBindingConfiguration();
            var results = config.Query();

            return results.ToList();
        }

        private X509Certificate2 GetCertificateFromStore(string storeName, string thumbprint)
        {
            X509Store store = new X509Store(storeName, StoreLocation.LocalMachine, OpenFlags.ReadOnly);

            return store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).OfType<X509Certificate2>().FirstOrDefault();
        }

        private void ReplaceFirewallRules(HostingSettingsRollbackContext rollback)
        {
            this.DeleteFirewallRules(rollback);

            INetFwPolicy2 firewallPolicy = GetFirewallPolicyObject();
            INetFwRule firewallRule = CreateNetFwRule($"{this.HttpPort},{this.HttpsPort}");

            firewallPolicy.Rules.Add(firewallRule);

            rollback.RollbackActions.Add(() => firewallPolicy.Rules.Remove(firewallRule.Name));
        }

        private INetFwRule CreateNetFwRule(string ports)
        {
            INetFwRule firewallRule = CreateFirewallRuleInstance();

            firewallRule.ApplicationName = this.pathProvider.GetFullPath(Constants.ServiceExeName, this.pathProvider.AppPath);
            firewallRule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
            firewallRule.Description = "Permits access to the Lithnet Access Manager Web Service";
            firewallRule.Enabled = true;
            firewallRule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN;
            firewallRule.Protocol = 6; //TCP
            firewallRule.LocalPorts = ports;
            firewallRule.InterfaceTypes = "All";
            firewallRule.Name = Constants.FirewallRuleName;
            return firewallRule;
        }

        private static INetFwRule CreateFirewallRuleInstance()
        {
            var firewallRuleType = Type.GetTypeFromProgID("HNetCfg.FWRule");

            if (firewallRuleType == null)
            {
                throw new InvalidOperationException("Unable to find type 'HNetCfg.FWRule'");
            }

            INetFwRule firewallRule = (INetFwRule)Activator.CreateInstance(firewallRuleType);

            if (firewallRule == null)
            {
                throw new InvalidOperationException("Unable to create type 'HNetCfg.FWRule'");
            }

            return firewallRule;
        }

        private static INetFwPolicy2 GetFirewallPolicyObject()
        {
            var firewallPolicyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");

            if (firewallPolicyType == null)
            {
                throw new InvalidOperationException("Unable to find type 'HNetCfg.FwPolicy2'");
            }

            INetFwPolicy2 firewallPolicy = (INetFwPolicy2)Activator.CreateInstance(firewallPolicyType);

            if (firewallPolicyType == null)
            {
                throw new InvalidOperationException("Unable to find type 'HNetCfg.FwPolicy2'");
            }

            return firewallPolicy;
        }

        private void DeleteFirewallRules(HostingSettingsRollbackContext rollback)
        {
            INetFwPolicy2 firewallPolicy = GetFirewallPolicyObject();

            try
            {
                var existingFirewallRule = firewallPolicy.Rules.Item(Constants.FirewallRuleName);
                firewallPolicy.Rules.Remove(Constants.FirewallRuleName);
                rollback.RollbackActions.Add(() => firewallPolicy.Rules.Add(this.CreateNetFwRule(existingFirewallRule.LocalPorts)));
            }
            catch
            {
                // ignore
            }
        }

        private async Task PollServiceStatus(CancellationToken token)
        {
            try
            {
                Debug.WriteLine("Poll started");
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
                    this.serviceSettings.ServiceController.Refresh();

                    switch (this.serviceSettings.ServiceController.Status)
                    {
                        case ServiceControllerStatus.StartPending:
                            this.ServiceStatus = "Starting";

                            break;

                        case ServiceControllerStatus.StopPending:
                            this.ServiceStatus = "Stopping";
                            break;

                        case ServiceControllerStatus.ContinuePending:
                            this.ServiceStatus = "Continue pending";

                            break;

                        case ServiceControllerStatus.PausePending:
                            this.ServiceStatus = "Pausing";
                            break;

                        default:
                            this.ServiceStatus = this.serviceSettings.ServiceController.Status.ToString();
                            break;
                    }

                    this.ServicePending = this.serviceSettings.ServiceController.Status ==
                                          ServiceControllerStatus.ContinuePending ||
                                          this.serviceSettings.ServiceController.Status ==
                                          ServiceControllerStatus.PausePending ||
                                          this.serviceSettings.ServiceController.Status ==
                                          ServiceControllerStatus.StartPending ||
                                          this.serviceSettings.ServiceController.Status ==
                                          ServiceControllerStatus.StopPending;
                }
            }
            catch
            {
                this.ServicePending = false;
                this.ServiceStatus = "Unknown";
            }

            Debug.WriteLine("Poll stopped");
        }


        private void UpdateCertificateBinding(HostingSettingsRollbackContext rollback)
        {
            var bindingConfiguration = new CertificateBindingConfiguration();
            var originalBinding = this.GetCertificateBinding(bindingConfiguration);

            if (originalBinding != null)
            {
                bindingConfiguration.Delete(originalBinding.IpPort);
                rollback.RollbackActions.Add(() => bindingConfiguration.Bind(originalBinding));
            }

            CertificateBinding binding = new CertificateBinding(this.Certificate.Thumbprint, "My", new IPEndPoint(IPAddress.Parse("0.0.0.0"), this.WorkingModel.HttpSys.HttpsPort), HttpSysHostingOptions.AppId, new BindingOptions());
            bindingConfiguration.Bind(binding);
            rollback.RollbackActions.Add(() => bindingConfiguration.Delete(binding.IpPort));
        }

        private CertificateBinding GetCertificateBinding(CertificateBindingConfiguration config)
        {
            foreach (var binding in config.Query())
            {
                if (binding.AppId == HttpSysHostingOptions.AppId)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}