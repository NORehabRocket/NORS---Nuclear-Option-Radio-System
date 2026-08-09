using System;

namespace NORS.Plugin.Server
{
    /// <summary>
    /// Tells the plugin whether this process is a headless dedicated game server rather than a
    /// player's client.
    ///
    /// This matters because everything else NORS does assumes a client: a local player to resolve,
    /// a microphone to open, an IMGUI panel to draw, a Steam client to ask for an id. None of that
    /// exists on a dedicated server, so the normal hub must not start there at all — it would throw
    /// on the first frame. Instead the plugin runs the voice relay in-process, so the game server
    /// carries its own players' voice with no separate service to deploy.
    /// </summary>
    internal static class ServerMode
    {
        private static bool _resolved;
        private static bool _isDedicated;
        private static int _gamePort = -1;

        /// <summary>True when the game was launched with -DedicatedServer.</summary>
        public static bool IsDedicatedServer
        {
            get
            {
                if (_resolved) return _isDedicated;
                _resolved = true;
                _isDedicated = DetectFromCommandLine();
                return _isDedicated;
            }
        }

        /// <summary>
        /// The port this game server listens on: the game's own -port switch, or its default.
        /// Read from the command line because the networking singletons don't exist yet when the
        /// plugin loads, and because it must match what clients see without any coordination.
        /// </summary>
        public static int GamePort
        {
            get
            {
                if (_gamePort >= 0) return _gamePort;
                _gamePort = 7777;   // UdpSocketFactory's default when -port isn't given
                try
                {
                    string[] args = Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length - 1; i++)
                        if (string.Equals(args[i], "-port", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(args[i + 1], out int p) && p > 0 && p <= 65535)
                        { _gamePort = p; break; }
                }
                catch { }
                return _gamePort;
            }
        }

        /// <summary>
        /// The command line is the only signal available at plugin-load time — the networking
        /// singletons don't exist yet, so we can't ask whether a server is running. This is the
        /// same switch the game's own CommandLineArgParser reads.
        /// </summary>
        private static bool DetectFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], "-DedicatedServer", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch (Exception e)
            {
                NorsPlugin.Log.LogWarning($"NORS: could not read the command line ({e.Message}); assuming client.");
            }
            return false;
        }
    }
}
