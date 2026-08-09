using System;

namespace NORS.Common
{
    /// <summary>
    /// Wire protocol constants shared by the in-game plugin and the standalone relay server.
    /// This assembly is deliberately game-agnostic: positions are plain floats in the game's
    /// origin-independent "global" frame (see GlobalPosition on the plugin side), so the server
    /// never needs to know anything about Unity or Nuclear Option.
    /// </summary>
    public static class NorsProtocol
    {
        /// <summary>Bumped on any breaking change to packet layout. Mismatched clients are rejected.</summary>
        public const byte Version = 2;   // v2: multi-room (roomId + isHost in Hello/State)

        public const int DefaultPort = 5555;

        /// <summary>
        /// A server hosting voice in-process binds its game port plus this offset, and clients
        /// probe the same. That way several game servers on ONE machine (7777, 7778, ...) get
        /// their own relay (8777, 8778, ...) with no port clash and nothing to configure, and a
        /// client always reaches the relay belonging to the server it is actually playing on.
        /// Offset by 1000 rather than 1 precisely because operators run servers on consecutive
        /// ports — +1 would collide with the next game server.
        /// </summary>
        public const int RelayPortOffset = 1000;

        /// <summary>Derives the voice port for a game server listening on <paramref name="gamePort"/>.</summary>
        public static int RelayPortFor(int gamePort)
        {
            if (gamePort <= 0) return DefaultPort;
            int p = gamePort + RelayPortOffset;
            return (p > 0 && p <= 65535) ? p : DefaultPort;
        }

        /// <summary>
        /// Room key for a game server endpoint. Clients on the same game server agree on this
        /// without being told, which keeps two servers sharing one relay from hearing each other.
        /// Never returns 0, because the relay treats 0 as "not in a session" and drops the audio.
        /// </summary>
        public static ulong RoomFor(string address, int gamePort)
        {
            ulong h = 1469598103934665603UL;                  // FNV-1a 64
            if (!string.IsNullOrEmpty(address))
                foreach (char c in address) { h ^= char.ToLowerInvariant(c); h *= 1099511628211UL; }
            h ^= (ulong)gamePort; h *= 1099511628211UL;
            return h == 0 ? 1UL : h;
        }

        // ---- Audio pipeline (must match on every client; the server is codec-agnostic) ----
        public const int SampleRate = 48000;       // Hz, matches Unity's mixer well
        public const int FrameSamples = 960;       // 20 ms @ 48 kHz, mono
        public const int Channels = 1;
        public const double FrameSeconds = FrameSamples / (double)SampleRate;

        // ---- Limits ----
        public const int MaxAudioBytes = 1200;     // keep a voice datagram under a typical MTU
        public const int MaxPlayerNameLen = 32;
        public const int MaxRxFrequencies = 12;    // radios a client can monitor at once
        public const int MaxDatagram = 1400;

        // ---- Timing ----
        public const float StateSendInterval = 0.2f;   // 5 Hz position/freq updates
        public const float HelloRetryInterval = 1.0f;
        public const float PingInterval = 3.0f;
        public const float ClientTimeoutSeconds = 10f; // server drops a silent client after this
    }

    public enum PacketType : byte
    {
        Hello = 1,     // client -> server: announce identity
        HelloAck = 2,  // server -> client: accepted, here's the server name
        State = 3,     // client -> server: faction, position, monitored frequencies
        Voice = 4,     // client -> server -> clients: one encoded audio frame
        Ping = 5,      // client -> server
        Pong = 6,      // server -> client
        Bye = 7,       // client -> server: leaving
        Reject = 8,    // server -> client: protocol mismatch / banned at join
        Kicked = 9,    // server -> client: kicked/banned by admin while connected
        Roster = 10,   // server -> clients: list of connected players (for vote/admin UI)
        VoteKick = 11, // client -> server: cast a vote to kick a target
        Notice = 12,   // server -> clients: short on-screen message (votes, results, MOTD)
        AdminAuth = 13,// client -> server: authenticate for remote admin
        AdminResult = 14, // server -> client: auth/command outcome
        AdminCommand = 15, // client -> server: privileged kick/ban/unban
        BanList = 16,  // P2P only: game host -> peers, the set of muted/banned Steam ids to ignore
    }

    public enum AdminOp : byte
    {
        Kick = 0,
        Ban = 1,
        Unban = 2,
    }

    public enum Modulation : byte
    {
        AM = 0,        // amplitude modulation (VHF/UHF aviation & military) - more terrain sensitive
        FM = 1,        // frequency modulation (low-band tactical) - more terrain tolerant
        Disabled = 2,  // radio off
    }

    /// <summary>
    /// Deterministic 32-bit hash. Used to turn a faction name into a stable id that matches
    /// across machines (unlike <see cref="string.GetHashCode"/>, which is randomized per process
    /// on modern .NET). Also used to derive encryption-net key ids from faction identity.
    /// </summary>
    public static class NorsHash
    {
        public static int Fnv1a(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint h = offset;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= prime;
                }
                return (int)h;
            }
        }
    }
}
