using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NORS.Common;
using NORS.Plugin.Comms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NORS.Plugin.UI
{
    /// <summary>
    /// Adds NORS as a selectable page in the cockpit's <see cref="VirtualMFD"/> bezel menu: it claims
    /// a FREE bezel button (the game disables unused ones), clones an existing page label for exact
    /// style/placement, and toggles its own NORS content panel — so it never overlaps the other pages.
    /// We never touch the game's existing pages, so nothing is renamed or broken. Falls back (returns
    /// false) on cockpits with no VirtualMFD or no free button, so the hub can use the corner overlay.
    /// </summary>
    internal sealed class MfdBezelPage
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo FLeftButtons = typeof(VirtualMFD).GetField("leftButtons", NP);
        private static readonly FieldInfo FRightButtons = typeof(VirtualMFD).GetField("rightButtons", NP);
        private static readonly FieldInfo FLeftScreens = typeof(VirtualMFD).GetField("leftScreens", NP);
        private static readonly FieldInfo FRightScreens = typeof(VirtualMFD).GetField("rightScreens", NP);

        private VirtualMFD _vmfd;
        private Button _button;
        private UnityAction _toggle;
        private GameObject _label;
        private GameObject _panel;
        private Text _body;
        private bool _bound, _visible;
        private readonly StringBuilder _sb = new StringBuilder(256);

        /// <summary>Returns true if NORS is (now) living on the bezel; false means "use the overlay instead".</summary>
        public bool Tick(RadioSet radios, List<string> talkers, bool jammed)
        {
            VirtualMFD current = _vmfd != null ? _vmfd : Object.FindObjectOfType<VirtualMFD>();
            if (current == null) { Teardown(); return false; }
            if (current != _vmfd) { Teardown(); _vmfd = current; }

            if (!_bound && !TryBuild()) return false;

            if (_visible && _body != null) _body.text = Compose(radios, talkers, jammed);
            return true;
        }

        private bool TryBuild()
        {
            var lb = FLeftButtons?.GetValue(_vmfd) as List<Button>;
            var rb = FRightButtons?.GetValue(_vmfd) as List<Button>;
            var ls = FLeftScreens?.GetValue(_vmfd) as List<MFDScreen>;
            var rs = FRightScreens?.GetValue(_vmfd) as List<MFDScreen>;

            // Prefer a free button on a side that has an existing page to copy style/geometry from.
            if (FindSlot(rb, rs, out Button freeBtn, out MFDScreen refScreen, out Button refBtn) ||
                FindSlot(lb, ls, out freeBtn, out refScreen, out refBtn))
            {
                Build(freeBtn, refScreen, refBtn);
                return _bound;
            }
            return false;
        }

        private static bool FindSlot(List<Button> buttons, List<MFDScreen> screens, out Button freeBtn, out MFDScreen refScreen, out Button refBtn)
        {
            freeBtn = null; refScreen = null; refBtn = null;
            if (buttons == null || screens == null) return false;

            for (int i = 0; i < screens.Count && i < buttons.Count; i++)
                if (screens[i] != null) { refScreen = screens[i]; refBtn = buttons[i]; break; }
            if (refScreen == null || refScreen.label == null) return false;   // need a reference page

            for (int i = 0; i < buttons.Count; i++)
            {
                bool free = i >= screens.Count || screens[i] == null;
                if (free && buttons[i] != null) { freeBtn = buttons[i]; return true; }
            }
            return false;
        }

        private void Build(Button freeBtn, MFDScreen refScreen, Button refBtn)
        {
            Text refLabel = refScreen.label;
            Canvas canvas = refLabel.canvas;
            if (canvas == null) return;

            // Clone the existing page label → identical font/size/style, then move it to the free
            // button's row using the same label-to-button offset the other rows use.
            _label = Object.Instantiate(refLabel.gameObject, refLabel.transform.parent);
            _label.name = "NORS_Label";
            var lblText = _label.GetComponent<Text>();
            if (lblText != null) lblText.text = NorsConfig.MfdPageLabel.Value;
            if (refBtn != null)
                _label.transform.position = freeBtn.transform.position + (refLabel.transform.position - refBtn.transform.position);
            _label.SetActive(true);

            // Content panel (hidden until the button is pressed).
            _panel = new GameObject("NORS_Page", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _panel.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = new Vector2(0.17f, 0.10f);
            prt.anchorMax = new Vector2(0.98f, 0.88f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0f, 0.03f, 0f, 0.85f);
            _panel.transform.SetAsLastSibling();

            var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            bodyGo.transform.SetParent(_panel.transform, false);
            var brt = (RectTransform)bodyGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(14f, 10f); brt.offsetMax = new Vector2(-14f, -10f);
            _body = bodyGo.GetComponent<Text>();
            _body.font = refLabel.font;
            _body.color = refLabel.color;
            _body.fontSize = Mathf.Max(12, Mathf.RoundToInt(refLabel.fontSize * 0.9f * NorsConfig.MfdFontScale.Value));
            _body.alignment = TextAnchor.UpperLeft;
            _body.raycastTarget = false;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.supportRichText = true;

            _panel.SetActive(false);
            _visible = false;

            // Claim the free button: disable its (dead) prefab handler, add our toggle.
            for (int i = 0; i < freeBtn.onClick.GetPersistentEventCount(); i++)
                freeBtn.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            _toggle = TogglePage;
            freeBtn.onClick.AddListener(_toggle);
            freeBtn.enabled = true;
            freeBtn.gameObject.SetActive(true);
            _button = freeBtn;

            _bound = true;
        }

        private void TogglePage()
        {
            _visible = !_visible;
            if (_panel != null) _panel.SetActive(_visible);
        }

        public void Teardown()
        {
            if (_button != null && _toggle != null) { try { _button.onClick.RemoveListener(_toggle); } catch { } }
            if (_panel != null) Object.Destroy(_panel);
            if (_label != null) Object.Destroy(_label);
            _button = null; _toggle = null; _panel = null; _label = null; _body = null;
            _bound = false; _visible = false;
            _vmfd = null;
        }

        private string Compose(RadioSet radios, List<string> talkers, bool jammed)
        {
            _sb.Length = 0;
            _sb.Append("NORS RADIO");
            if (jammed) _sb.Append("   <color=#ff5050>JAMMED</color>");
            _sb.Append("\n\n");

            if (radios != null)
                for (int i = 0; i < radios.Radios.Count; i++)
                {
                    var r = radios.Radios[i];
                    _sb.Append(r.Label).Append("  ").Append(r.FreqMHz.ToString("000.000")).Append("  ")
                       .Append(r.Mod == Modulation.AM ? "AM" : r.Mod == Modulation.FM ? "FM" : "OFF");
                    if (r.Secure) _sb.Append("  SEC");
                    if (!r.Rx) _sb.Append("  (rx off)");
                    if (i == radios.TxIndex) _sb.Append("   <color=#ff5050>TX</color>");
                    _sb.Append('\n');
                }

            _sb.Append("\nReceiving: ");
            if (talkers == null || talkers.Count == 0) _sb.Append("--");
            else
                for (int i = 0; i < talkers.Count && i < 6; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    string t = talkers[i];
                    int sp = t.IndexOf("  ", System.StringComparison.Ordinal);
                    _sb.Append(sp > 0 ? t.Substring(0, sp) : t);
                }
            return _sb.ToString();
        }
    }
}
