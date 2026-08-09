using NORS.Common;

namespace NORS.Plugin.Net
{
    internal enum AutoPhase
    {
        Idle,           // not in a mission
        ProbingRelay,   // trying the relay, waiting to see if anything answers
        UsingRelay,
        UsingP2P,
        Stuck,          // neither works here — the panel explains why
    }

    /// <summary>
    /// Picks the voice transport by trying it, instead of making the player know which one their
    /// server supports.
    ///
    /// The two transports fail in opposite places, and neither failure is visible up front: Steam
    /// P2P needs every player's Steam id, which a UDP-socket server never sends, and the relay
    /// needs a relay to actually be running. So on joining, NORS asks the relay first — if the
    /// game server hosts voice in-process, its own address answers within a second or two — and
    /// falls back to P2P when nothing does.
    ///
    /// Relay wins when both are available, deliberately. NORS sends uncompressed audio (~280
    /// kbit/s per listener), and in P2P the person talking uploads one copy per listener, so on
    /// anything bigger than a small lobby the relay is the difference between working and not.
    ///
    /// The probe is just the normal Hello/HelloAck handshake — no second mechanism to maintain,
    /// and no extra traffic beyond one connection attempt.
    /// </summary>
    internal sealed class TransportSelector
    {
        /// <summary>How long to wait for a relay to answer before giving up on it.</summary>
        public const float ProbeSeconds = 4f;

        /// <summary>How long before we re-check whether a relay appeared (server restarted, etc).</summary>
        private const float RetryAfterSeconds = 45f;

        public AutoPhase Phase { get; private set; } = AutoPhase.Idle;

        private float _probeStarted;
        private float _nextRetry;
        private bool _loggedChoice;

        public bool WantsRelay => Phase == AutoPhase.ProbingRelay || Phase == AutoPhase.UsingRelay;

        /// <summary>Leaving a mission: the next server gets a fresh decision.</summary>
        public void Reset()
        {
            Phase = AutoPhase.Idle;
            _nextRetry = 0f;
            _loggedChoice = false;
        }

        /// <summary>
        /// Drives the decision. <paramref name="relayCandidate"/> is whether we have an address to
        /// try at all; <paramref name="relayConnected"/> is whether the relay handshake completed;
        /// <paramref name="p2pUsable"/> is whether Steam P2P has anyone it can actually address.
        /// </summary>
        public void Tick(float now, bool inGame, bool relayCandidate, bool relayConnected, bool p2pUsable)
        {
            if (!inGame) { if (Phase != AutoPhase.Idle) Reset(); return; }

            switch (Phase)
            {
                case AutoPhase.Idle:
                    StartProbeOrP2P(now, relayCandidate, p2pUsable);
                    break;

                case AutoPhase.ProbingRelay:
                    if (relayConnected) { Phase = AutoPhase.UsingRelay; Announce(); }
                    else if (now - _probeStarted >= ProbeSeconds)
                    {
                        // Nothing answered. Fall back rather than sitting on a dead socket.
                        Phase = p2pUsable ? AutoPhase.UsingP2P : AutoPhase.Stuck;
                        _nextRetry = now + RetryAfterSeconds;
                        Announce();
                    }
                    break;

                case AutoPhase.UsingRelay:
                    // The relay went away (server restart / crash): re-decide rather than go mute.
                    if (!relayConnected && now - _probeStarted >= ProbeSeconds)
                    {
                        _loggedChoice = false;
                        StartProbeOrP2P(now, relayCandidate, p2pUsable);
                    }
                    break;

                case AutoPhase.UsingP2P:
                    // A relay may have come up after we settled (server restarted with NORS on).
                    // Only re-check when P2P isn't actually carrying anyone, so we never interrupt
                    // working voice to go looking for something better.
                    if (!p2pUsable && relayCandidate && now >= _nextRetry)
                    {
                        _loggedChoice = false;
                        BeginProbe(now);
                    }
                    break;

                case AutoPhase.Stuck:
                    if (p2pUsable) { Phase = AutoPhase.UsingP2P; _loggedChoice = false; Announce(); }
                    else if (relayCandidate && now >= _nextRetry) { _loggedChoice = false; BeginProbe(now); }
                    break;
            }
        }

        private void StartProbeOrP2P(float now, bool relayCandidate, bool p2pUsable)
        {
            if (relayCandidate) BeginProbe(now);
            else { Phase = p2pUsable ? AutoPhase.UsingP2P : AutoPhase.Stuck; Announce(); }
        }

        private void BeginProbe(float now)
        {
            Phase = AutoPhase.ProbingRelay;
            _probeStarted = now;
        }

        private void Announce()
        {
            if (_loggedChoice) return;
            _loggedChoice = true;
            switch (Phase)
            {
                case AutoPhase.UsingRelay:
                    NorsPlugin.Log.LogInfo("NORS: this server hosts voice — using the relay.");
                    break;
                case AutoPhase.UsingP2P:
                    NorsPlugin.Log.LogInfo("NORS: no relay here — using direct Steam P2P voice.");
                    break;
                case AutoPhase.Stuck:
                    NorsPlugin.Log.LogWarning(
                        "NORS: no voice transport works on this server. It isn't running a NORS relay, " +
                        "and it doesn't share Steam ids so P2P has nobody to reach. Ask the operator to " +
                        "install NORS server-side, or set Transport = Relay with a ServerHost by hand.");
                    break;
            }
        }

        /// <summary>Short status for the radio panel.</summary>
        public string Describe(int relayPort)
        {
            switch (Phase)
            {
                case AutoPhase.ProbingRelay: return "looking for a voice server…";
                case AutoPhase.UsingRelay: return $"server voice (port {relayPort})";
                case AutoPhase.UsingP2P: return "direct P2P";
                case AutoPhase.Stuck: return "no voice available here";
                default: return "";
            }
        }
    }
}
