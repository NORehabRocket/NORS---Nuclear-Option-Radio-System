namespace NORS.Plugin
{
    /// <summary>
    /// One place to ask "is the player typing in the game's chat box?".
    ///
    /// Every NORS input path is gated on this: the hotkeys (PTT / panel / cycle TX /
    /// tune / MFD toggle) and the radio UI itself. Otherwise a chat message
    /// containing "," or "." silently retunes your radio and "y" switches which
    /// radio transmits — which looks exactly like "the mod stopped working".
    /// (Reported by the community; the hotkey half came from @Appulcake's PR.)
    /// </summary>
    internal static class GameInput
    {
        public static bool ChatOpen
        {
            get
            {
                try { return CursorManager.GetFlag(CursorFlags.Chat); }
                catch (System.Exception) { return false; }
            }
        }
    }
}
