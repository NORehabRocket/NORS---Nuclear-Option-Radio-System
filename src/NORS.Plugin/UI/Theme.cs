using UnityEngine;

namespace NORS.Plugin.UI
{
    /// <summary>
    /// Shared DarkSkies look for the in-game IMGUI panels, matching the web panels
    /// (dark navy, cyan accents). Styles are built lazily on the first OnGUI and
    /// cached — Unity forbids creating textures/styles outside the GUI thread.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg = new Color32(0x0d, 0x14, 0x1c, 0xF2);
        public static readonly Color Panel = new Color32(0x11, 0x1b, 0x26, 0xFF);
        public static readonly Color Line = new Color32(0x1c, 0x2a, 0x3a, 0xFF);
        public static readonly Color Txt = new Color32(0xc9, 0xd6, 0xe2, 0xFF);
        public static readonly Color Dim = new Color32(0x64, 0x78, 0x8c, 0xFF);
        public static readonly Color Cyan = new Color32(0x35, 0xc8, 0xff, 0xFF);
        public static readonly Color Green = new Color32(0x2e, 0xe6, 0xa8, 0xFF);
        public static readonly Color Amber = new Color32(0xff, 0xb8, 0x4d, 0xFF);
        public static readonly Color Red = new Color32(0xff, 0x4d, 0x5e, 0xFF);

        private static bool _built;
        private static Texture2D _bgTex, _panelTex, _lineTex, _txTex, _cryptoTex;

        public static GUIStyle Window, Header, Label, LabelDim, Row, RowTx, RowCrypto, Chip, Value;

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public static void Build()
        {
            if (_built) return;
            _built = true;

            _bgTex = Solid(Bg);
            _panelTex = Solid(Panel);
            _lineTex = Solid(Line);
            _txTex = Solid(new Color(0.10f, 0.30f, 0.22f, 1f));      // TX row tint (green-ish)
            _cryptoTex = Solid(new Color(0.24f, 0.18f, 0.03f, 1f));  // encrypted row tint (amber-ish)

            Window = new GUIStyle(GUI.skin.window);
            Window.normal.background = _bgTex;
            Window.onNormal.background = _bgTex;
            Window.normal.textColor = Cyan;
            Window.onNormal.textColor = Cyan;
            Window.fontStyle = FontStyle.Bold;
            Window.padding = new RectOffset(10, 10, 22, 10);
            Window.border = new RectOffset(6, 6, 6, 6);

            Header = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                richText = true,
                padding = new RectOffset(2, 2, 2, 2),
            };
            Header.normal.textColor = Dim;

            Label = new GUIStyle(GUI.skin.label) { richText = true, padding = new RectOffset(2, 2, 1, 1) };
            Label.normal.textColor = Txt;

            LabelDim = new GUIStyle(Label);
            LabelDim.normal.textColor = Dim;

            Value = new GUIStyle(Label) { fontStyle = FontStyle.Bold };
            Value.normal.textColor = Txt;

            Row = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(0, 0, 2, 0) };
            Row.normal.background = _panelTex;

            RowTx = new GUIStyle(Row);
            RowTx.normal.background = _txTex;

            RowCrypto = new GUIStyle(Row);
            RowCrypto.normal.background = _cryptoTex;

            Chip = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, richText = true };
            Chip.normal.textColor = Dim;
        }
    }
}
