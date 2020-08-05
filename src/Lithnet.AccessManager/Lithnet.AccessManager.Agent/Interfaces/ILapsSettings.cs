﻿namespace Lithnet.AccessManager.Agent
{
    public interface ILapsSettings
    {
        string CertThumbprint { get; }

        bool Enabled { get; }

        int PasswordLength { get; }

        string PasswordCharacters { get; }

        bool UseUpper { get; }

        bool UseLower { get; }

        bool UseSymbol { get; }

        bool UseNumeric { get; }

        bool UseReadabilitySeparator { get; }

        string ReadabilitySeparator { get; }

        int ReadabilitySeparatorInterval { get; }

        int PasswordHistoryDaysToKeep { get; }

        bool WriteToMsMcsAdmPasswordAttributes { get; }

        int MaximumPasswordAge { get; }

        bool WriteToLithnetAttributes { get; }
    }
}