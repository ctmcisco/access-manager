﻿namespace Lithnet.AccessManager.Agent
{
    internal class EventIDs 
    {
        public const int AgentStarted = 1000;
        public const int AgentDisabled = 1001;
        public const int LapsAgentDisabled = 1002;
        public const int JitAgentDisabled = 1003;
        public const int JitAgentEnabled = 1019;
        public const int LapsAgentEnabled = 1020;

        public const int RunningOnDC = 1004;
        public const int JitUnexpectedException = 1005;
        public const int LapsUnexpectedException = 1006;
        public const int AgentUnexpectedException = 1007;

        public const int LapsAgentNotConfigured = 1008;
        public const int LapsConflict = 1009;

        public const int PasswordExpired = 1010;
        public const int SetPasswordOnLapsAttribute = 1011;
        public const int SetPasswordOnAmAttribute = 1012;
        public const int SetPassword = 1013;

        public const int PasswordExpiryCheckFailure = 1014;
        public const int PasswordChangeFailure = 1015;

        public const int JitGroupFound = 1016;
        public const int LapsConflictResolved = 1017;

        public const int PasswordChangeNotRequired = 1018;

    }
}
