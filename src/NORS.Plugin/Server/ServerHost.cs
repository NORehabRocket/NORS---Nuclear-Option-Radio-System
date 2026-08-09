using System;
using System.IO;
using BepInEx;
using NORS.Common;
using NORS.Server.Core;

namespace NORS.Plugin.Server
{
    /// <summary>
    /// Runs the voice relay inside the dedicated game server process.
    ///
    /// This is the same <see cref="RelayServer"/> the standalone relay runs — the project
    /// multi-targets netstandard2.1 so Unity's Mono runtime can load it — so there is one relay
    /// implementation, not two. Hosting it here means a community deploys nothing extra: if the
    /// game server is up, voice is up, and clients find it at the game server's own address
    /// because that is the address they are already connected to.
    ///
    /// Why relay rather than peer-to-peer on a busy server: NORS sends uncompressed 16 kHz PCM
    /// (see RawVoiceCodec — Opus can't load on the game's Mono runtime), which is ~280 kbit/s per
    /// destination. In P2P the person talking uploads one copy per listener, so a 30-player server
    /// asks a pilot's home connection for ~8 Mbit/s the moment they key the mic. Through the relay
    /// they send one copy and the server, which has the bandwidth, does the fan-out.
    ///
    /// Nothing client-side is touched here: no audio device, no local player, no panel.
    /// </summary>
    internal sealed class ServerHost
    {
        private RelayServer _relay;

        public bool Running => _relay != null && _relay.Running;
        public int Port => _relay?.Port ?? 0;

        public void Start()
        {
            if (_relay != null) return;
            try
            {
                // 0 = derive from this server's game port, so several servers on one box each
                // get their own voice port automatically (7777->8777, 7778->8778) and clients
                // reach the relay for the server they're actually on.
                int configured = NorsConfig.ServerRelayPort.Value;
                int port = configured > 0 ? configured : NorsProtocol.RelayPortFor(ServerMode.GamePort);
                string name = NorsConfig.ServerRelayName.Value;
                if (string.IsNullOrEmpty(name)) name = "Nuclear Option server";

                // Bans live next to the config so they survive updates, and so an operator can
                // edit or seed the file by hand (steamid|ip). Pointing several servers at one
                // shared path is how bans sync between them - the file IS the sync mechanism.
                string shared = (NorsConfig.ServerSharedBanFile.Value ?? "").Trim();
                string banFile = shared.Length > 0
                    ? shared
                    : Path.Combine(Paths.ConfigPath, "nors-server-bans.txt");
                if (shared.Length > 0)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(banFile);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    }
                    catch (Exception e)
                    {
                        NorsPlugin.Log.LogWarning($"NORS: shared ban folder is not usable ({e.Message}); " +
                                                  "bans will still work but won't sync.");
                    }
                }
                string adminPassword = NorsConfig.ServerAdminPassword.Value;

                _relay = new RelayServer(port, name, banFile, adminPassword,
                    NorsConfig.ServerVoteKick.Value);
                _relay.Log += line => NorsPlugin.Log.LogInfo("NORS relay: " + line);
                _relay.Start();

                NorsPlugin.Log.LogInfo(
                    $"NORS is hosting voice for this server on UDP {port} (game port {ServerMode.GamePort}). " +
                    "Clients running NORS 0.7.7+ find it automatically — nobody needs to type an address.");
                if (configured > 0)
                    NorsPlugin.Log.LogWarning(
                        $"NORS: Server/RelayPort is pinned to {port}. Auto-discovery expects " +
                        $"{NorsProtocol.RelayPortFor(ServerMode.GamePort)}, so players must set ServerPort " +
                        "by hand. Leave it at 0 unless you need a specific port.");
                if (shared.Length > 0)
                    NorsPlugin.Log.LogInfo($"NORS: sharing voice bans via {banFile} - bans made on any " +
                                           "server using this file apply here within a few seconds.");
                if (string.IsNullOrEmpty(adminPassword))
                    NorsPlugin.Log.LogInfo(
                        "NORS: no Server/AdminPassword set, so remote voice moderation is off. " +
                        "Set one (and add moderator Steam ids to Moderation/Moderators) to enable it.");
            }
            catch (Exception e)
            {
                _relay = null;
                NorsPlugin.Log.LogError(
                    $"NORS could not start the voice relay on this server: {e.Message}. " +
                    "Players will fall back to whatever transport they have configured.");
            }
        }

        public void Stop()
        {
            try { _relay?.Stop(); } catch { }
            _relay = null;
        }
    }
}
