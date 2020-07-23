﻿using Lithnet.AccessManager.Server.Configuration;
using MahApps.Metro.IconPacks;
using Stylet;

namespace Lithnet.AccessManager.Server.UI
{
    public sealed class AuditingViewModel : Conductor<PropertyChangedBase>.Collection.OneActive
    {
        public AuditingViewModel(AuditOptions model,
            INotificationChannelDefinitionsViewModelFactory<PowershellNotificationChannelDefinition, PowershellNotificationChannelDefinitionViewModel> psFactory,
            INotificationChannelDefinitionsViewModelFactory<SmtpNotificationChannelDefinition, SmtpNotificationChannelDefinitionViewModel> smtpFactory,
            INotificationChannelDefinitionsViewModelFactory<WebhookNotificationChannelDefinition, WebhookNotificationChannelDefinitionViewModel> whFactory,
            INotificationChannelSelectionViewModelFactory notificationChannelSelectionViewModelFactory
            )
        {
            this.DisplayName = "Auditing";

            this.Powershell = psFactory.CreateViewModel(model.NotificationChannels.Powershell);
            this.Webhook = whFactory.CreateViewModel(model.NotificationChannels.Webhooks);
            this.Smtp = smtpFactory.CreateViewModel(model.NotificationChannels.Smtp);

            this.Items.Add(this.Smtp);
            this.Items.Add(this.Webhook);
            this.Items.Add(this.Powershell);

            this.ActivateItem(this.Smtp);

            this.Notifications = notificationChannelSelectionViewModelFactory.CreateViewModel(model.GlobalNotifications);
        }

        public PackIconUniconsKind Icon => PackIconUniconsKind.FileExclamationAlt;

        public NotificationChannelSelectionViewModel Notifications { get; }

        private NotificationChannelDefinitionsViewModel<PowershellNotificationChannelDefinition, PowershellNotificationChannelDefinitionViewModel> Powershell { get; }

        private NotificationChannelDefinitionsViewModel<WebhookNotificationChannelDefinition, WebhookNotificationChannelDefinitionViewModel> Webhook { get; }

        private NotificationChannelDefinitionsViewModel<SmtpNotificationChannelDefinition, SmtpNotificationChannelDefinitionViewModel> Smtp { get; }
    }
}
