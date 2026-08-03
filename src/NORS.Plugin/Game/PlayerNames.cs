using System;
using NuclearOption.Networking;

namespace NORS.Plugin.Game
{
    /// <summary>
    /// Single point of contact for the game's player-name API.
    ///
    /// It has moved twice in quick succession:
    ///   0.33    Player.PlayerName (plain string)
    ///   0.34    Player.GetNameOrCensored()
    ///   0.34.x  Player.GetDisplayName(PlayerNameContext)
    ///
    /// One helper means the next rename is a one-line fix the compiler points
    /// straight at, and a runtime failure degrades to an empty name instead of
    /// throwing inside the voice path.
    /// </summary>
    internal static class PlayerNames
    {
        public static string Get(Player p)
        {
            if (p == null) return "";
            try
            {
                // "Other" is what the game uses for HUD/callsign display.
                return p.GetDisplayName(PlayerNameContext.Other) ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
