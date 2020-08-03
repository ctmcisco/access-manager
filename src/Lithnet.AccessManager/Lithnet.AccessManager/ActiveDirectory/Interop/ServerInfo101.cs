﻿using System.Runtime.InteropServices;

namespace Lithnet.AccessManager.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ServerInfo101
    {
        public ServerPlatform PlatformId;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        public int VersionMajor;

        public int VersionMinor;

        public ServerTypes Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Comment;
    }
}
