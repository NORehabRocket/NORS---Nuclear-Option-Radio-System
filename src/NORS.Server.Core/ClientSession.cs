using System.Collections.Generic;
using System.Net;

namespace NORS.Server.Core
{
    /// <summary>One connected radio client as seen by the relay.</summary>
    public sealed class ClientSession
    {
        public uint Id;
        public ulong SteamId;
        public ulong Room;        // the game host's SteamID; 0 = not in a session yet
        public bool IsHost;       // this client is the host (moderator) of its room
        public IPEndPoint EndPoint;
        public string Name = "?";
        public int FactionId;
        public float X, Y, Z;
        public bool IsAdmin;      // master admin (relay operator, via password)

        /// <summary>Frequencies (kHz) this client is currently monitoring. Voice on any of these is forwarded to it.</summary>
        public readonly HashSet<int> RxFrequencies = new HashSet<int>();

        public double LastSeen;

        public string Ip => EndPoint != null ? EndPoint.Address.ToString() : "?";

        public override string ToString() => $"{Name} (#{Id}, faction {FactionId}, {RxFrequencies.Count} freqs)";
    }

    /// <summary>Immutable snapshot of a client for the admin UI/console (no locking needed by readers).</summary>
    public struct ClientInfo
    {
        public uint Id;
        public ulong SteamId;
        public ulong Room;
        public bool IsHost;
        public string Name;
        public int FactionId;
        public string Ip;
        public int Port;
        public int FreqCount;
        public double IdleSeconds;
    }
}
