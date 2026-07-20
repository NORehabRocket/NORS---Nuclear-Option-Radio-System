using System;

namespace NORS.Common
{
    /// <summary>
    /// Static encode/decode for every packet. Each datagram starts with [version][type].
    /// The server only ever needs to inspect Hello/State/Voice headers; it forwards Voice
    /// datagrams verbatim, so the audio payload is opaque to it.
    /// </summary>
    public static class Packets
    {
        public static void WriteHeader(PacketWriter w, PacketType type)
        {
            w.Reset();
            w.Byte(NorsProtocol.Version);
            w.Byte((byte)type);
        }

        // ---------------- Hello ----------------
        // roomId = the game host's SteamID (the "server" this client belongs to). isHost = this
        // client is the game host (and thus moderator of its room).
        public static void WriteHello(PacketWriter w, uint clientId, ulong steamId, ulong roomId, bool isHost, int factionId, string playerName)
        {
            WriteHeader(w, PacketType.Hello);
            w.UInt(clientId);
            w.ULong(steamId);
            w.ULong(roomId);
            w.Bool(isHost);
            w.Int(factionId);
            w.Str(playerName);
        }

        public static void ReadHello(ref PacketReader r, out uint clientId, out ulong steamId, out ulong roomId, out bool isHost, out int factionId, out string playerName)
        {
            clientId = r.UInt();
            steamId = r.ULong();
            roomId = r.ULong();
            isHost = r.Bool();
            factionId = r.Int();
            playerName = r.Str();
        }

        // ---------------- HelloAck / Reject / Kicked ----------------
        public static void WriteHelloAck(PacketWriter w, string serverName)
        {
            WriteHeader(w, PacketType.HelloAck);
            w.Str(serverName);
        }

        public static void WriteReject(PacketWriter w, string reason)
        {
            WriteHeader(w, PacketType.Reject);
            w.Str(reason);
        }

        public static void WriteKicked(PacketWriter w, string reason)
        {
            WriteHeader(w, PacketType.Kicked);
            w.Str(reason);
        }

        // ---------------- State ----------------
        public static void WriteState(PacketWriter w, uint clientId, ulong steamId, ulong roomId, bool isHost, int factionId,
            float x, float y, float z, int[] rxFreqKHz, int rxCount, string playerName)
        {
            WriteHeader(w, PacketType.State);
            w.UInt(clientId);
            w.ULong(steamId);
            w.ULong(roomId);
            w.Bool(isHost);
            w.Int(factionId);
            w.Float(x); w.Float(y); w.Float(z);
            int count = Math.Min(rxCount, NorsProtocol.MaxRxFrequencies);
            w.Byte((byte)count);
            for (int i = 0; i < count; i++) w.Int(rxFreqKHz[i]);
            w.Str(playerName);
        }

        // ---------------- Voice ----------------
        // Layout: clientId u32, seq u32, txFreqKHz i32, mod u8, factionId i32, cryptoKeyId u8,
        //         x f32, y f32, z f32, audioLen u16, audio[], callsign str, txJam u8.
        // The relay only needs the leading clientId + txFreq to route, and forwards the datagram
        // verbatim, so trailing fields stay opaque to it. txJam is the transmitter's own jam level
        // (0..255 = 0..1) so a jammed sender's signal is garbled for ALL receivers; it's trailing +
        // optional so old clients (which omit it) stay wire-compatible.
        public static void WriteVoice(PacketWriter w, uint clientId, uint seq, int txFreqKHz,
            Modulation mod, int factionId, byte cryptoKeyId, float x, float y, float z,
            byte[] audio, int audioOffset, int audioLen, string callsign, byte txJam)
        {
            WriteHeader(w, PacketType.Voice);
            w.UInt(clientId);
            w.UInt(seq);
            w.Int(txFreqKHz);
            w.Byte((byte)mod);
            w.Int(factionId);
            w.Byte(cryptoKeyId);
            w.Float(x); w.Float(y); w.Float(z);
            w.UShort((ushort)audioLen);
            w.Bytes(audio, audioOffset, audioLen);
            w.Str(callsign);
            w.Byte(txJam);
        }

        // ---------------- Roster (server -> clients) ----------------
        public static void WriteRosterHeader(PacketWriter w, int count)
        {
            WriteHeader(w, PacketType.Roster);
            w.UShort((ushort)count);
        }

        public static void WriteRosterEntry(PacketWriter w, uint clientId, int factionId, string name)
        {
            w.UInt(clientId);
            w.Int(factionId);
            w.Str(name);
        }

        public static int ReadRosterCount(ref PacketReader r) => r.UShort();

        public static RosterEntry ReadRosterEntry(ref PacketReader r)
        {
            RosterEntry e;
            e.ClientId = r.UInt();
            e.FactionId = r.Int();
            e.Name = r.Str();
            e.SteamId = 0;   // relay roster doesn't carry Steam ids; set only in P2P mode
            return e;
        }

        // ---------------- VoteKick (client -> server) ----------------
        public static void WriteVoteKick(PacketWriter w, uint voterClientId, uint targetClientId)
        {
            WriteHeader(w, PacketType.VoteKick);
            w.UInt(voterClientId);
            w.UInt(targetClientId);
        }

        // ---------------- Notice (server -> clients) ----------------
        public static void WriteNotice(PacketWriter w, string message)
        {
            WriteHeader(w, PacketType.Notice);
            w.Str(message);
        }

        // ---------------- Admin ----------------
        public static void WriteAdminAuth(PacketWriter w, uint clientId, string password)
        {
            WriteHeader(w, PacketType.AdminAuth);
            w.UInt(clientId);
            w.Str(password);
        }

        public static void WriteAdminResult(PacketWriter w, bool ok, bool isAuth, string message)
        {
            WriteHeader(w, PacketType.AdminResult);
            w.Bool(ok);
            w.Bool(isAuth);
            w.Str(message);
        }

        public static void WriteAdminCommand(PacketWriter w, uint clientId, AdminOp op, uint targetClientId, string arg)
        {
            WriteHeader(w, PacketType.AdminCommand);
            w.UInt(clientId);
            w.Byte((byte)op);
            w.UInt(targetClientId);
            w.Str(arg);
        }

        // ---------------- BanList (P2P: host -> peers) ----------------
        public static void WriteBanList(PacketWriter w, ulong[] steamIds, int count)
        {
            WriteHeader(w, PacketType.BanList);
            int n = System.Math.Min(count, 255);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) w.ULong(steamIds[i]);
        }

        public static ulong[] ReadBanList(ref PacketReader r)
        {
            int n = r.Byte();
            var ids = new ulong[n];
            for (int i = 0; i < n; i++) ids[i] = r.ULong();
            return ids;
        }
    }

    public struct RosterEntry
    {
        public uint ClientId;
        public int FactionId;
        public string Name;
        public ulong SteamId;   // set in P2P mode (roster built from the game); 0 from the relay roster
    }

    /// <summary>Parsed voice header the client needs. Audio is copied into <see cref="Audio"/> (length <see cref="AudioLen"/>).</summary>
    public struct VoiceHeader
    {
        public uint ClientId;
        public uint Seq;
        public int TxFreqKHz;
        public Modulation Mod;
        public int FactionId;
        public byte CryptoKeyId;
        public float X, Y, Z;
        public int AudioLen;
        public byte[] Audio;
        public string Callsign;
        public float TxJam01;   // transmitter's own jam level 0..1 (0 if sender is an older client)

        /// <summary>Reads a Voice packet body (reader already positioned past [version][type]).</summary>
        public static VoiceHeader Read(ref PacketReader r)
        {
            VoiceHeader v;
            v.ClientId = r.UInt();
            v.Seq = r.UInt();
            v.TxFreqKHz = r.Int();
            v.Mod = (Modulation)r.Byte();
            v.FactionId = r.Int();
            v.CryptoKeyId = r.Byte();
            v.X = r.Float(); v.Y = r.Float(); v.Z = r.Float();
            v.AudioLen = r.UShort();
            v.Audio = new byte[v.AudioLen];
            r.Bytes(v.Audio, 0, v.AudioLen);
            v.Callsign = r.Remaining > 0 ? r.Str() : string.Empty;
            v.TxJam01 = r.Remaining > 0 ? r.Byte() / 255f : 0f;
            return v;
        }
    }
}
