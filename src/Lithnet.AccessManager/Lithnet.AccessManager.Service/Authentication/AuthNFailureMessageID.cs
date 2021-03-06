﻿namespace Lithnet.AccessManager.Service.Internal
{
    public enum AuthNFailureMessageID
    {
        UnknownFailure = 0,
        SsoIdentityNotFound = 1,
        ExternalAuthNProviderDenied = 2,
        ExternalAuthNProviderError = 3,
        InvalidCertificate = 4,
    }
}
