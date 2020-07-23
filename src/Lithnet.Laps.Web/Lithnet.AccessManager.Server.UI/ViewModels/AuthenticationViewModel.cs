﻿using System;
using System.Collections.Generic;
using System.Linq;
using Lithnet.AccessManager.Server.Configuration;
using MahApps.Metro.IconPacks;
using Stylet;

namespace Lithnet.AccessManager.Server.UI
{
    public class AuthenticationViewModel : PropertyChangedBase, IHaveDisplayName
    {
        private readonly AuthenticationOptions model;

        private readonly INotifiableEventPublisher eventPublisher;

        public AuthenticationViewModel(AuthenticationOptions model, INotifiableEventPublisher eventPublisher)
        {
            this.eventPublisher = eventPublisher;
            this.model = model;

            model.Iwa ??= new IwaAuthenticationProviderOptions();
            model.Oidc ??= new OidcAuthenticationProviderOptions();
            model.WsFed ??= new WsFedAuthenticationProviderOptions();
            this.eventPublisher.Register(this);
        }

        [NotifiableProperty]
        public AuthenticationMode AuthenticationMode { get => this.model.Mode; set => this.model.Mode = value; }

        public IEnumerable<AuthenticationMode> AuthenticationModeValues
        {
            get
            {
                return Enum.GetValues(typeof(AuthenticationMode)).Cast<AuthenticationMode>();
            }
        }

        [NotifiableProperty]
        public AuthenticationSchemes IwaAuthenticationSchemes { get => this.model.Iwa.AuthenticationSchemes; set => this.model.Iwa.AuthenticationSchemes = value; }

        public IEnumerable<AuthenticationSchemes> IwaAuthenticationSchemesValues
        {
            get
            {
                return Enum.GetValues(typeof(AuthenticationSchemes)).Cast<AuthenticationSchemes>().Where(t => t > 0);
            }
        }

        public bool OidcVisible => this.AuthenticationMode == AuthenticationMode.Oidc;

        public bool WsFedVisible => this.AuthenticationMode == AuthenticationMode.WsFed;

        public bool IwaVisible => this.AuthenticationMode == AuthenticationMode.Iwa;

        [NotifiableProperty] 
        public string OidcAuthority { get => this.model.Oidc.Authority; set => this.model.Oidc.Authority = value; }

        [NotifiableProperty]
        public string OidcClientID { get => this.model.Oidc.ClientID; set => this.model.Oidc.ClientID = value; }

        [NotifiableProperty]
        public string OidcSecret { get => this.model.Oidc.Secret; set => this.model.Oidc.Secret = value; }

        [NotifiableProperty]
        public string WsFedRealm { get => this.model.WsFed.Realm; set => this.model.WsFed.Realm = value; }

        [NotifiableProperty]
        public string WsFedMetadata { get => this.model.WsFed.Metadata; set => this.model.WsFed.Metadata = value; }

        public string DisplayName { get; set; } = "Authentication";

        public PackIconUniconsKind Icon => PackIconUniconsKind.User;
    }
}
