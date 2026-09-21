using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace NORS.Plugin.UI
{
    /// <summary>
    /// Small NORS mark in a corner of the main menu, with the running version beside it.
    ///
    /// The version is the point of it. A stale install is otherwise invisible until something
    /// misbehaves in a match — which is exactly how DarkSkies issue #2 went unexplained for a month,
    /// with a months-old build failing every frame while the player had no way to see what they were
    /// running. Being able to read "NORS 0.7.9" before pressing Play turns "which version are you on?"
    /// from a support conversation into a glance.
    ///
    /// Menu only: in flight this would be clutter over the cockpit, and the HUD readout already says
    /// NORS is alive.
    /// </summary>
    internal sealed class MenuBadge
    {
        private const string ResourceName = "NORS.Plugin.Resources.nors-badge.png";

        private Texture2D _tex;
        private bool _tried;          // load once; a failure must not retry every OnGUI
        private GUIStyle _label;

        /// <summary>Drawn size in points. The embedded art is 128px, so this stays crisp.</summary>
        private const float Size = 72f;
        private const float Margin = 16f;

        public void Render(bool inGame)
        {
            if (inGame || !NorsConfig.MenuBadge.Value) return;

            EnsureLoaded();
            if (_tex == null) return;

            if (_label == null)
                _label = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 11,
                };

            string text = $"<color={Theme.Hex(Theme.Green)}>NORS</color> " +
                          $"<color=#999999>{NorsPlugin.Version}</color>";
            Vector2 textSize = _label.CalcSize(new GUIContent(text));

            float w = Size + 6f + textSize.x;
            float h = Mathf.Max(Size, textSize.y);
            Rect area = CornerRect(NorsConfig.MenuBadgeCorner.Value, w, h);

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);   // present, not shouting
            GUI.DrawTexture(new Rect(area.x, area.y + (h - Size) * 0.5f, Size, Size), _tex, ScaleMode.ScaleToFit);
            GUI.color = prev;

            GUI.Label(new Rect(area.x + Size + 6f, area.y, textSize.x, h), text, _label);
        }

        private static Rect CornerRect(MfdCorner corner, float w, float h)
        {
            float x = Margin, y = Margin;
            switch (corner)
            {
                case MfdCorner.TopRight:    x = Screen.width - w - Margin; y = Margin; break;
                case MfdCorner.BottomLeft:  x = Margin;                    y = Screen.height - h - Margin; break;
                case MfdCorner.BottomRight: x = Screen.width - w - Margin; y = Screen.height - h - Margin; break;
                default:                    x = Margin;                    y = Margin; break;   // TopLeft
            }
            return new Rect(x, y, w, h);
        }

        private void EnsureLoaded()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (s == null)
                    {
                        NorsPlugin.Log.LogWarning("NORS: menu badge resource missing: " + ResourceName);
                        return;
                    }
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    // Dimensions are replaced by LoadImage; 2x2 is just a valid placeholder.
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,   // survives scene loads, never serialised
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    if (!t.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(t);
                        NorsPlugin.Log.LogWarning("NORS: menu badge failed to decode.");
                        return;
                    }
                    _tex = t;
                }
            }
            catch (Exception e)
            {
                NorsPlugin.Log.LogWarning("NORS: menu badge load failed: " + e.Message);
            }
        }
    }
}
