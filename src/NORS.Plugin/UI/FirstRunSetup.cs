using UnityEngine;

namespace NORS.Plugin.UI
{
    /// <summary>
    /// First-launch popup (shown at the main menu): asks the player to bind
    /// push-to-talk. Exists because PTT ships unbound — T is the game's chat
    /// key, so any letter default fights the chat box (community PR #1).
    /// </summary>
    internal sealed class FirstRunSetup
    {
        private Rect _win = new Rect(0, 0, 440, 0);
        private bool _positioned;
        private KeyCode _picked = KeyCode.None;   // nothing pre-selected: the player chooses

        /// <summary>Show until answered, but only outside a mission (main menu).</summary>
        public bool ShouldShow(bool inGame)
        {
            if (NorsConfig.PttSetupDone.Value) return false;
            if (NorsConfig.PttKey.Value != KeyCode.None) return false; // upgraders keep their key, no popup
            return !inGame;
        }

        public void Render()
        {
            if (!_positioned)
            {
                _positioned = true;
                _win.x = (Screen.width - _win.width) / 2f;
                _win.y = Screen.height * 0.26f;
            }
            _win = GUILayout.Window(770231, _win, Draw, "NORS RADIO — FIRST-TIME SETUP");
        }

        private void Draw(int id)
        {
            GUILayout.Label("Welcome to NORS voice radio!");
            GUILayout.Label("Pick your PUSH-TO-TALK key. Press any key now, or use a quick pick:");
            GUILayout.Space(6);

            // Keyboard capture via the IMGUI event stream (legacy-input-safe).
            // Never while the chat box has the keyboard, or typing would bind a key.
            Event e = Event.current;
            if (!GameInput.ChatOpen && e != null && e.type == EventType.KeyDown
                && e.keyCode != KeyCode.None && e.keyCode != KeyCode.Escape)
            {
                _picked = e.keyCode;
                e.Use();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CAPS LOCK")) _picked = KeyCode.CapsLock;
            if (GUILayout.Button("~  (tilde)")) _picked = KeyCode.BackQuote;
            if (GUILayout.Button("MOUSE 4")) _picked = KeyCode.Mouse3;
            if (GUILayout.Button("MOUSE 5")) _picked = KeyCode.Mouse4;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label(_picked == KeyCode.None
                ? "(Tip: avoid T — that's the game's chat key.)"
                : $"Selected: {_picked}");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.enabled = _picked != KeyCode.None;
            if (GUILayout.Button($"BIND {(_picked == KeyCode.None ? "..." : _picked.ToString())}"))
            {
                NorsConfig.PttKey.Value = _picked;
                NorsConfig.PttSetupDone.Value = true;
                NorsPlugin.Log.LogInfo($"NORS: push-to-talk bound to {_picked}.");
            }
            GUI.enabled = true;
            // Skipping leaves PTT unbound on purpose — we don't pick a key for the player.
            // It isn't a silent failure: the panel warns in red with one-click binds, and the
            // popup re-arms on the next launch until a key is actually chosen.
            if (GUILayout.Button("Skip for now"))
            {
                NorsConfig.PttSetupDone.Value = true;
                NorsPlugin.Log.LogWarning(NorsConfig.PttKey.Value == KeyCode.None
                    ? "NORS: push-to-talk left unbound — you cannot transmit until you set one " +
                      "(F7 panel, or F1 > NORS > Input)."
                    : $"NORS: push-to-talk left as {NorsConfig.PttKey.Value}.");
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}
