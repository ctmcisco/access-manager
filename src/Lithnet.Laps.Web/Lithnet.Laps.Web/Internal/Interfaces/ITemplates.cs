﻿namespace Lithnet.Laps.Web.Internal
{
    public interface ITemplates
    {
        string LogSuccessTemplate { get; }

        string LogFailureTemplate { get; }

        string EmailSuccessTemplate { get; }

        string EmailFailureTemplate { get; }
    }
}
