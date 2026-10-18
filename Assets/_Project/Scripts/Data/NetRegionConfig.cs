using System;
using UnityEngine;

namespace Arena.Data
{
    /// <summary>
    /// One server region of <see cref="NetConfig"/> (Server-Spec §6.3, §10): a host name or IP and the UDP port. An empty
    /// <see cref="Host"/> is the "Local (LAN)" region — the server is found by the <c>Discover</c> broadcast.
    /// </summary>
    [Serializable]
    public sealed class NetRegionConfig
    {
        [Tooltip("Stable id of the region (the name key is region.<id>).")]
        public string Id;

        [Tooltip("Localization key of the region name.")]
        public string NameKey;

        [Tooltip("Host name or IP of the server; empty — LAN discovery.")]
        public string Host;

        [Tooltip("UDP port of the game server.")]
        public int Port;

        public bool IsLan => string.IsNullOrEmpty(Host);
    }
}
