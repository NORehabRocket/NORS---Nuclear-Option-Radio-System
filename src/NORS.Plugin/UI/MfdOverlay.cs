using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NORS.Common;
using NORS.Plugin.Comms;
using NORS.Plugin.Game;
using UnityEngine;
using UnityEngine.UI;

namespace NORS.Plugin.UI
{
    internal enum MfdLayoutKind { Fill, Corner, Hud }

    /// <summary>
    /// Puts the NORS readout on the cockpit MFD by taking over the airframe's systems/weapon panel —
    /// the named child under <c>TacScreen/Canvas</c> (e.g. "SystemStatus") that already sits in the
    /// right place per aircraft. We hide that panel's children + kill its layout, then parent the
    /// readout filling the same rect. Text best-fits so it never clips. The original content is
    /// restored when NORS is toggled off. Technique mirrors clumzy/NO_Tactitools' WeaponDisplay (incl.
    /// the SFB-81 rotation). Falls back to a canvas corner if the panel can't be found. Re-binds on
    /// aircraft change.
    /// </summary>
    internal sealed class MfdOverlay
    {
        private static readonly FieldInfo CanvasField =
            typeof(TacScreen).GetField("canvas", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CockpitTacScreenField =
            typeof(Cockpit).GetField("tacScreen", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HudCanvasField =
            typeof(FlightHud).GetField("canvas", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FuelBarField =
            typeof(FuelGauge).GetField("fuelBar", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ThrottleBarField =
            typeof(ThrottleGauge).GetField("throttleBar", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Vector3[] _corners = new Vector3[4];

        private Transform _attached;     // what we parented onto (null => needs (re)bind, e.g. aircraft change)
        private GameObject _go;
        private Text _text;

        // Takeover bookkeeping (to restore the original content on teardown).
        private Transform _panel;
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private Behaviour _layoutGroup, _sizeFitter;
        private Quaternion _panelOrigRot;
        private bool _rotatedPanel;

        private float _builtScale;
        private MfdLayoutKind _builtKind;
        private RectTransform _hudBar;   // fuel/throttle bar we track (per-frame; HUD elements move)
        private readonly StringBuilder _sb = new StringBuilder(256);

        public void Tick(LocalState local, RadioSet radios, List<string> talkers, bool jammed)
        {
            if (!NorsConfig.MfdEnabled.Value || local == null || !local.InGame)
            {
                Teardown();
                return;
            }

            if (_attached == null)
            {
                Teardown();
                Transform target = ResolveTarget(out MfdLayoutKind kind);
                if (target != null) Build(target, kind);
            }
            if (_go == null) return;

            if (!Mathf.Approximately(_builtScale, NorsConfig.MfdFontScale.Value))
            {
                Teardown();
                Transform target = ResolveTarget(out MfdLayoutKind kind);
                if (target != null) Build(target, kind);
                if (_text == null) return;
            }

            // The game's HUD cluster is re-positioned every frame (it floats with the camera/head),
            // so a one-time placement drifts off the gauge — follow the fuel bar each tick.
            if (_builtKind == MfdLayoutKind.Hud) FollowHudBar();

            _text.text = Compose(radios, talkers, jammed);
        }

        /// <summary>Keep the readout glued just above the fuel/throttle bar, in HUD-canvas space.</summary>
        private void FollowHudBar()
        {
            if (_hudBar == null) _hudBar = GetHudGaugeBar();
            if (_hudBar == null || !_hudBar.gameObject.activeInHierarchy) return;

            var rt = (RectTransform)_go.transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;

            _hudBar.GetWorldCorners(_corners);                 // 0=BL 1=TL 2=TR 3=BR
            Vector3 topCentre = (_corners[1] + _corners[2]) * 0.5f;
            Vector3 local = parent.InverseTransformPoint(topCentre);
            local.z = 0f;
            rt.localPosition = local + new Vector3(
                NorsConfig.MfdHudOffsetX.Value, NorsConfig.MfdHudOffsetY.Value + 10f, 0f);
        }

        /// <summary>Pick the panel to take over: the airframe's systems/weapon panel, else a canvas corner.</summary>
        private Transform ResolveTarget(out MfdLayoutKind kind)
        {
            // Always draw on the HUD glass (Overlay/BezelPage modes are temporarily disabled). Anchoring
            // to the HUD canvas (a persistent singleton) — not a per-aircraft gauge — avoids grabbing
            // other mods' HUD elements (the GCAS-arrow attach bug) and surviving stale across swaps.
            Transform hud = GetHudCanvas();
            if (hud != null) { kind = MfdLayoutKind.Hud; return hud; }

            var tac = GetLocalTacScreen();
            if (tac != null)
            {
                Transform canvas = GetCanvas(tac);
                if (canvas != null)
                {
                    // Reliable everywhere: overlay a corner of the radar scope (always visible, and its
                    // corners are empty). Non-destructive — we don't hide the radar.
                    Transform radar = canvas.Find("Radar");
                    if (radar != null) { kind = MfdLayoutKind.Corner; return radar; }

                    // Fallback: take over the airframe's systems/weapon panel.
                    Transform p = FindSystemsPanel(canvas, GetPlatform());
                    if (p != null) { kind = MfdLayoutKind.Fill; return p; }
                    kind = MfdLayoutKind.Corner; return canvas;
                }
            }

            kind = MfdLayoutKind.Corner;
            return null;
        }

        private void Build(Transform target, MfdLayoutKind kind)
        {
            Font font = null;
            Color color = new Color(0.55f, 1f, 0.6f);
            var sample = target.GetComponentInChildren<Text>(true);
            if (sample != null) { font = sample.font; color = sample.color; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            }
            // On the HUD, force the standard green (the FUEL label we sampled is yellow at low fuel).
            if (kind == MfdLayoutKind.Hud) color = new Color(0.55f, 1f, 0.6f);

            bool takeover = kind == MfdLayoutKind.Fill;
            if (takeover) LogPanel(GetPlatform(), target);
            Vector2 panelSize = new Vector2(240f, 180f);
            if (takeover)
            {
                var prt0 = target as RectTransform;          // capture size BEFORE hiding content
                if (prt0 != null) panelSize = prt0.rect.size;
                _panel = target;
                TakeOverPanel(target);
            }

            _go = new GameObject("NORS_MFD", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _go.transform.SetParent(target, false);
            var bg = _go.GetComponent<Image>();
            // On the HUD: no background (match the transparent green HUD). On a cockpit screen: opaque.
            bg.color = kind == MfdLayoutKind.Hud ? new Color(0f, 0f, 0f, 0f) : new Color(0f, 0.03f, 0f, 0.92f);
            bg.raycastTarget = false;
            var rt = (RectTransform)_go.transform;
            switch (kind)
            {
                case MfdLayoutKind.Fill: FixedBox(rt, panelSize); break;
                case MfdLayoutKind.Hud: AnchorHud(rt); break;
                default: LayoutCorner(rt, NorsConfig.MfdCornerPos.Value); break;
            }
            _go.AddComponent<RectMask2D>();   // clip the readout to its box

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(_go.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 6f); trt.offsetMax = new Vector2(-8f, -6f);

            _text = textGo.GetComponent<Text>();
            _text.font = font;
            _text.color = color;
            _text.lineSpacing = 1f;
            _text.raycastTarget = false;
            // HUD sits just above the fuel bar, so bottom-align the text to hug it; panels top-align.
            _text.alignment = kind == MfdLayoutKind.Hud ? TextAnchor.LowerLeft : TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            _text.supportRichText = true;
            float scale = Mathf.Clamp(NorsConfig.MfdFontScale.Value, 0.4f, 3f);
            _text.resizeTextForBestFit = true;
            _text.resizeTextMinSize = 6;
            float maxBase = kind == MfdLayoutKind.Hud ? 19f : 22f;   // a bit smaller on the HUD
            _text.resizeTextMaxSize = Mathf.Clamp(Mathf.RoundToInt(maxBase * scale), 8, 48);

            // The SFB-81 weapon panel is mounted rotated; match NO_Tactitools when we land on it.
            if (kind == MfdLayoutKind.Fill && GetPlatform() == "SFB-81")
            {
                _panelOrigRot = target.localRotation;
                target.localRotation = Quaternion.Euler(0f, 0f, -90f);
                _rotatedPanel = true;
            }

            _attached = target;
            _builtScale = NorsConfig.MfdFontScale.Value;
            _builtKind = kind;
            _hudBar = null;   // re-resolve the bar for the new aircraft
        }

        private static string GetPlatform()
        {
            try
            {
                if (GameManager.GetLocalAircraft(out var ac) && ac != null)
                    return ac.GetAircraftParameters()?.aircraftName;
            }
            catch { }
            return null;
        }

        /// <summary>The LOCAL aircraft's rendered TacScreen. FindObjectOfType can return a non-rendered
        /// instance (so we'd draw on an invisible cockpit → blank); the local Cockpit.tacScreen is the
        /// one actually shown.</summary>
        private static TacScreen GetLocalTacScreen()
        {
            try
            {
                if (GameManager.GetLocalAircraft(out var ac) && ac != null)
                {
                    var cockpit = ac.GetComponentInChildren<Cockpit>(true);
                    if (cockpit != null && CockpitTacScreenField != null
                        && CockpitTacScreenField.GetValue(cockpit) is TacScreen ts && ts != null)
                        return ts;
                }
            }
            catch { }
            return Object.FindObjectOfType<TacScreen>();
        }

        /// <summary>The HUD glass canvas (FlightHud SceneSingleton's private 'canvas').</summary>
        private static Transform GetHudCanvas()
        {
            try
            {
                var hud = SceneSingleton<FlightHud>.i;
                if (hud == null) return null;
                Canvas c = null;
                try { c = HudCanvasField?.GetValue(hud) as Canvas; } catch { }
                if (c == null) c = hud.GetComponentInChildren<Canvas>(true);
                return c != null ? c.transform : null;
            }
            catch { return null; }
        }

        private static Transform GetCanvas(TacScreen tac)
        {
            Transform t = tac.transform.Find("Canvas");
            if (t != null) return t;
            Canvas c = null;
            try { c = CanvasField?.GetValue(tac) as Canvas; } catch { }
            if (c == null) c = tac.GetComponentInChildren<Canvas>(true);
            return c != null ? c.transform : null;
        }

        private static Transform FindSystemsPanel(Transform canvas, string platform)
        {
            foreach (string path in PanelPaths(platform))
            {
                var t = canvas.Find(path);
                if (t != null) return t;
            }
            return null;
        }

        private static IEnumerable<string> PanelPaths(string platform)
        {
            switch (platform)
            {
                case "EW-1 Medusa": yield return "engPanel1"; break;
                case "CI-22 Cricket": yield return "EngPanel"; break;
                case "SAH-46 Chicane": yield return "BasicFlightInstrument"; break;
                case "VL-49 Tarantula": yield return "RightScreenBorder/WeaponPanel"; break;
                case "SFB-81": yield return "weaponPanel"; break;
                case "FastBomber1":
                case "AB-4 Alkyon":
                case "Alkyon AB-4": yield return "weaponPanel/frontProfile"; break;
                case "MiG-15": yield return "StatusGauges/FrontView"; break;
                case "F-16M King Viper": yield return "SystemsPanel"; break;
            }
            yield return "SystemStatus";
            yield return "SystemsPanel";
            yield return "weaponPanel";
            yield return "StatusGauges";
        }

        private void TakeOverPanel(Transform panel)
        {
            _layoutGroup = panel.GetComponent<LayoutGroup>();
            if (_layoutGroup != null) _layoutGroup.enabled = false;
            _sizeFitter = panel.GetComponent<ContentSizeFitter>();
            if (_sizeFitter != null) _sizeFitter.enabled = false;
            // NOTE: do NOT disable the panel's own Image — on modular cockpits (A-19, T/A-30) that Image
            // backs a UI Mask, and disabling it clips our readout to nothing (blank panel).

            _hidden.Clear();
            foreach (Transform child in panel)
            {
                if (child.gameObject.activeSelf)
                {
                    _hidden.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Box sized to the panel (captured before content was hidden, since the rect can
        /// collapse afterwards) and centered on it, clamped so it's neither tiny on huge panels (e.g.
        /// FastBomber's 800x800 silhouette) nor oversized on small ones.</summary>
        private static void FixedBox(RectTransform rt, Vector2 panelSize)
        {
            float w = Mathf.Clamp(Mathf.Abs(panelSize.x) * 0.92f, 150f, 440f);
            float h = Mathf.Clamp(Mathf.Abs(panelSize.y) * 0.92f, 90f, 320f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        /// <summary>Diagnostic dump so we can see each airframe's panel structure (to target it correctly).</summary>
        private static void LogPanel(string platform, Transform panel)
        {
            try
            {
                var sb = new StringBuilder();
                var prt = panel as RectTransform;
                var cv = panel.GetComponentInParent<Canvas>();
                sb.Append("[NORS-MFD] '").Append(platform ?? "?").Append("' panel='").Append(panel.name)
                  .Append("' active=").Append(panel.gameObject.activeInHierarchy);
                if (prt != null)
                    sb.Append(" rect=").Append((int)prt.rect.width).Append('x').Append((int)prt.rect.height)
                      .Append(" aPos=").Append(prt.anchoredPosition.ToString("F0"))
                      .Append(" pivot=").Append(prt.pivot.ToString("F2"))
                      .Append(" aMin=").Append(prt.anchorMin.ToString("F2")).Append(" aMax=").Append(prt.anchorMax.ToString("F2"))
                      .Append(" lScl=").Append(panel.localScale.ToString("F2"))
                      .Append(" wScl=").Append(panel.lossyScale.ToString("F3"));
                if (cv != null)
                {
                    sb.Append(" canvas='").Append(cv.name).Append("'/").Append(cv.renderMode);
                    if (cv.transform is RectTransform crt) sb.Append(" cRect=").Append((int)crt.rect.width).Append('x').Append((int)crt.rect.height);
                }
                Debug.Log(sb.ToString());
            }
            catch { }
        }

        /// <summary>Place the readout up by the FUEL/THROTTLE HUD cluster: we READ the gauge's on-screen
        /// position but keep the readout parented to the HUD canvas (not the gauge), so it sits with the
        /// HUD yet can't latch onto another mod's element (GCAS arrows) or go stale on a vehicle swap.
        /// Plus a user X/Y nudge to clear other mods' overlays. Falls back to a HUD corner if no gauge.</summary>
        private static void AnchorHud(RectTransform rt)
        {
            float s = Mathf.Clamp(NorsConfig.MfdFontScale.Value, 0.4f, 3f);
            float h = NorsConfig.MfdHudVerbose.Value ? 150f : 66f;   // compact = 1-2 lines
            rt.sizeDelta = new Vector2(300f * s, h * s);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            float ox = NorsConfig.MfdHudOffsetX.Value;
            float oy = NorsConfig.MfdHudOffsetY.Value;

            RectTransform bar = GetHudGaugeBar();
            if (bar != null)
            {
                // Sits just ABOVE the top of the fuel/throttle bar; actual position is maintained
                // every frame by FollowHudBar() since the HUD cluster moves with the camera.
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);              // bottom-centre pivot → grows upward
                return;
            }

            // Fallback: a HUD corner.
            const float inset = 24f;
            switch (NorsConfig.MfdHudCorner.Value)
            {
                case MfdCorner.TopLeft:
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(inset + ox, -inset + oy); break;
                case MfdCorner.TopRight:
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-inset + ox, -inset + oy); break;
                case MfdCorner.BottomRight:
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-inset + ox, inset + oy); break;
                default: // BottomLeft
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(inset + ox, inset + oy); break;
            }
        }

        /// <summary>The FUEL or THROTTLE bar's RectTransform on the HUD (read-only position reference).</summary>
        private static RectTransform GetHudGaugeBar()
        {
            try
            {
                var hud = SceneSingleton<FlightHud>.i;
                if (hud == null) return null;
                if (NorsConfig.MfdHudAnchor.Value == MfdHudAnchor.Throttle)
                {
                    var tg = hud.GetComponentInChildren<ThrottleGauge>(true);
                    if (tg != null && ThrottleBarField?.GetValue(tg) is Image tb && tb != null) return tb.rectTransform;
                }
                var fg = hud.GetComponentInChildren<FuelGauge>(true);
                if (fg != null && FuelBarField?.GetValue(fg) is Image fb && fb != null) return fb.rectTransform;
                return null;
            }
            catch { return null; }
        }

        private static void LayoutCorner(RectTransform rt, MfdCorner corner)
        {
            float w = Mathf.Clamp01(NorsConfig.MfdWidthFrac.Value);
            float h = Mathf.Clamp01(NorsConfig.MfdHeightFrac.Value);
            Vector2 min, max;
            switch (corner)
            {
                case MfdCorner.TopLeft: min = new Vector2(0f, 1f - h); max = new Vector2(w, 1f); break;
                case MfdCorner.TopRight: min = new Vector2(1f - w, 1f - h); max = new Vector2(1f, 1f); break;
                case MfdCorner.BottomLeft: min = new Vector2(0f, 0f); max = new Vector2(w, h); break;
                default: min = new Vector2(1f - w, 0f); max = new Vector2(1f, h); break;
            }
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private string Compose(RadioSet radios, List<string> talkers, bool jammed)
        {
            _sb.Length = 0;

            // Compact (default): just the active (selected) radio + who's transmitting — far less clutter
            // on the HUD, and easy to follow since you only cycle in one direction.
            if (!NorsConfig.MfdHudVerbose.Value)
            {
                var tx = radios != null ? radios.Tx : null;
                if (tx != null)
                {
                    _sb.Append(tx.Label).Append(' ').Append(tx.FreqMHz.ToString("000.000")).Append(' ').Append(ModShort(tx.Mod));
                    if (tx.Secure) _sb.Append(" S");
                }
                if (jammed) _sb.Append("  <color=#ff5050>JAM</color>");
                if (NorsConfig.MfdShowTalkers.Value && talkers != null && talkers.Count > 0)
                {
                    _sb.Append("\nRX ");
                    for (int i = 0; i < talkers.Count && i < 3; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        _sb.Append(ShortName(talkers[i]));
                    }
                }
                return _sb.ToString();
            }

            // Verbose: all radios + header.
            _sb.Append("NORS");
            if (jammed) _sb.Append("  <color=#ff5050>JAM</color>");
            _sb.Append('\n');
            if (radios != null)
            {
                for (int i = 0; i < radios.Radios.Count; i++)
                {
                    var r = radios.Radios[i];
                    _sb.Append(r.Label).Append(' ').Append(r.FreqMHz.ToString("000.000")).Append(' ').Append(ModShort(r.Mod));
                    if (r.Secure) _sb.Append(" S");
                    if (!r.Rx) _sb.Append(" x");
                    if (i == radios.TxIndex) _sb.Append("  <color=#ff5050>TX</color>");
                    _sb.Append('\n');
                }
            }
            if (NorsConfig.MfdShowTalkers.Value)
            {
                _sb.Append("RX ");
                if (talkers == null || talkers.Count == 0) _sb.Append("--");
                else
                    for (int i = 0; i < talkers.Count && i < 4; i++)
                    {
                        if (i > 0) _sb.Append(", ");
                        _sb.Append(ShortName(talkers[i]));
                    }
            }
            return _sb.ToString();
        }

        private static string ModShort(Modulation m) => m == Modulation.AM ? "AM" : m == Modulation.FM ? "FM" : "OFF";

        /// <summary>Talker strings look like "Name  ~67%  12km" — keep just the name for the MFD.</summary>
        private static string ShortName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int sp = s.IndexOf("  ", System.StringComparison.Ordinal);
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        public void Teardown()
        {
            if (_go != null) Object.Destroy(_go);
            _go = null;
            _text = null;

            if (_panel != null)
            {
                if (_rotatedPanel) _panel.localRotation = _panelOrigRot;
                foreach (var go in _hidden)
                    if (go != null) go.SetActive(true);
                if (_layoutGroup != null) _layoutGroup.enabled = true;
                if (_sizeFitter != null) _sizeFitter.enabled = true;
            }
            _hidden.Clear();
            _panel = null;
            _attached = null;
            _layoutGroup = null;
            _sizeFitter = null;
            _rotatedPanel = false;
            _hudBar = null;
        }
    }
}
