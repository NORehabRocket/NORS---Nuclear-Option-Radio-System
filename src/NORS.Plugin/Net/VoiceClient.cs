using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NORS.Common;

namespace NORS.Plugin.Net
{
    /// <summary>
    /// UDP transport to the relay server. Sends (Hello/State/Voice/Ping) happen on the Unity main
    /// thread; receives run on a background thread and drop parsed voice frames into a concurrent
    /// queue the main thread drains. The game's own (Mirage) netcode is never touched.
    /// </summary>
    internal sealed class VoiceClient
    {
        public bool Connected { get; private set; }
        public string ServerName { get; private set; } = "";
        public uint ClientId { get; private set; }
        public string LastError { get; private set; } = "";

        /// <summary>Set when the server kicks/bans/rejects us. The hub reads this to stop auto-reconnecting.</summary>
        public volatile bool Kicked;
        public string KickReason { get; private set; } = "";

        /// <summary>True once we've authenticated for remote admin on this server.</summary>
        public volatile bool AdminAuthed;

        private readonly List<RosterEntry> _roster = new List<RosterEntry>();
        private readonly object _rosterLock = new object();
        private readonly ConcurrentQueue<string> _notices = new ConcurrentQueue<string>();

        private Socket _socket;
        private IPEndPoint _server;
        private Thread _rxThread;
        private volatile bool _running;

        private readonly ConcurrentQueue<VoiceHeader> _voiceIn = new ConcurrentQueue<VoiceHeader>();
        private readonly PacketWriter _w = new PacketWriter();
        private readonly object _sendLock = new object();

        public void Start(string host, int port, uint clientId)
        {
            Stop();
            ClientId = clientId;
            Connected = false;
            LastError = "";
            Kicked = false;
            KickReason = "";
            AdminAuthed = false;
            lock (_rosterLock) _roster.Clear();
            while (_notices.TryDequeue(out _)) { }

            try
            {
                if (!IPAddress.TryParse(host, out var addr))
                {
                    var resolved = Dns.GetHostAddresses(host);
                    if (resolved.Length == 0) { LastError = "Cannot resolve host"; return; }
                    addr = resolved[0];
                }
                _server = new IPEndPoint(addr, port);

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { /* non-Windows */ }
                _socket.Connect(_server);

                _running = true;
                _rxThread = new Thread(RxLoop) { IsBackground = true, Name = "NORS-rx" };
                _rxThread.Start();
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Stop();
            }
        }

        public void Stop()
        {
            if (_socket != null && _running)
            {
                try { SendBye(); } catch { }
            }
            _running = false;
            Connected = false;
            AdminAuthed = false;
            try { _socket?.Close(); } catch { }
            _socket = null;
            try { _rxThread?.Join(200); } catch { }
            _rxThread = null;
            while (_voiceIn.TryDequeue(out _)) { }
            while (_notices.TryDequeue(out _)) { }
            lock (_rosterLock) _roster.Clear();
        }

        private void RxLoop()
        {
            var buf = new byte[NorsProtocol.MaxDatagram];
            while (_running)
            {
                try
                {
                    int n = _socket.Receive(buf);
                    if (n >= 2) Parse(buf, n);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException)
                {
                    // Server not up yet / transient; brief backoff.
                    Thread.Sleep(50);
                }
                catch { Thread.Sleep(50); }
            }
        }

        private void Parse(byte[] buf, int n)
        {
            var r = new PacketReader(buf, 0, n);
            byte version = r.Byte();
            if (version != NorsProtocol.Version) return;
            var type = (PacketType)r.Byte();

            switch (type)
            {
                case PacketType.HelloAck:
                    ServerName = r.Str();
                    Connected = true;
                    break;
                case PacketType.Reject:
                case PacketType.Kicked:
                    KickReason = r.Str();
                    LastError = KickReason;
                    Connected = false;
                    Kicked = true;
                    break;
                case PacketType.Pong:
                    break;
                case PacketType.Voice:
                    _voiceIn.Enqueue(VoiceHeader.Read(ref r));
                    break;
                case PacketType.Roster:
                    ReadRoster(ref r);
                    break;
                case PacketType.Notice:
                    _notices.Enqueue(r.Str());
                    break;
                case PacketType.AdminResult:
                    bool ok = r.Bool();
                    bool isAuth = r.Bool();
                    string msg = r.Str();
                    if (isAuth) AdminAuthed = ok;
                    _notices.Enqueue((ok ? "" : "[!] ") + msg);
                    break;
            }
        }

        private void ReadRoster(ref PacketReader r)
        {
            int count = Packets.ReadRosterCount(ref r);
            lock (_rosterLock)
            {
                _roster.Clear();
                for (int i = 0; i < count; i++) _roster.Add(Packets.ReadRosterEntry(ref r));
            }
        }

        public void GetRoster(List<RosterEntry> into)
        {
            into.Clear();
            lock (_rosterLock) into.AddRange(_roster);
        }

        public bool TryDequeueNotice(out string notice) => _notices.TryDequeue(out notice);

        public bool TryDequeueVoice(out VoiceHeader v) => _voiceIn.TryDequeue(out v);

        // -------- sends (main thread) --------

        public void SendHello(ulong steamId, ulong roomId, bool isHost, int factionId, string name)
        {
            lock (_sendLock)
            {
                Packets.WriteHello(_w, ClientId, steamId, roomId, isHost, factionId, name);
                Send();
            }
        }

        public void SendState(ulong steamId, ulong roomId, bool isHost, int factionId, float x, float y, float z, int[] rxFreqs, int rxCount, string name)
        {
            lock (_sendLock)
            {
                Packets.WriteState(_w, ClientId, steamId, roomId, isHost, factionId, x, y, z, rxFreqs, rxCount, name);
                Send();
            }
        }

        public void SendVoice(uint seq, int txFreqKHz, Modulation mod, int factionId, byte crypto,
            float x, float y, float z, byte[] audio, int audioLen, string callsign, byte txJam, int stableFactionId)
        {
            lock (_sendLock)
            {
                Packets.WriteVoice(_w, ClientId, seq, txFreqKHz, mod, factionId, crypto, x, y, z, audio, 0, audioLen, callsign, txJam, stableFactionId);
                Send();
            }
        }

        public void SendPing()
        {
            lock (_sendLock)
            {
                Packets.WriteHeader(_w, PacketType.Ping);
                _w.UInt(ClientId);
                Send();
            }
        }

        public void SendVoteKick(uint targetClientId)
        {
            lock (_sendLock) { Packets.WriteVoteKick(_w, ClientId, targetClientId); Send(); }
        }

        public void SendAdminAuth(string password)
        {
            lock (_sendLock) { Packets.WriteAdminAuth(_w, ClientId, password); Send(); }
        }

        public void SendAdminCommand(AdminOp op, uint targetClientId, string arg)
        {
            lock (_sendLock) { Packets.WriteAdminCommand(_w, ClientId, op, targetClientId, arg); Send(); }
        }

        private void SendBye()
        {
            lock (_sendLock)
            {
                Packets.WriteHeader(_w, PacketType.Bye);
                _w.UInt(ClientId);
                Send();
            }
        }

        private void Send()
        {
            try { _socket?.Send(_w.Buffer, 0, _w.Length, SocketFlags.None); }
            catch { }
        }
    }
}
