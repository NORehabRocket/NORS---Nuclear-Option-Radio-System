using UnityEngine;

namespace NORS.Plugin.Game
{
    /// <summary>
    /// Tracks how strongly the local aircraft's radio is being interfered with by radar jamming:
    ///  - enemy jamming-pods aimed at us (the unit's replicated <c>onJam</c> event), and
    ///  - our own active ECM / radar-jammer running (<c>GetECMIntensity</c>).
    /// The result is used to inject static into received voice — jamming the radar also garbles comms.
    /// </summary>
    internal sealed class JamSense
    {
        private Aircraft _sub;
        private float _lastJamTime = -999f;
        private float _lastJamAmount;

        public void Tick(Aircraft self)
        {
            if (self == _sub) return;
            Unsubscribe();
            _sub = self;
            if (self != null) { try { self.onJam += OnJam; } catch { } }
        }

        private void OnJam(Unit.JamEventArgs a)
        {
            _lastJamAmount = a.jamAmount;
            _lastJamTime = Time.unscaledTime;
        }

        /// <summary>0..1 interference on the local receiver right now (max of incoming jamming and own ECM).</summary>
        public float Level01(float reference)
        {
            float incoming = (Time.unscaledTime - _lastJamTime <= 0.6f) ? _lastJamAmount : 0f;
            float own = 0f;
            try { if (_sub != null) own = Mathf.Abs(_sub.GetECMIntensity()); } catch { }
            float raw = Mathf.Max(incoming, own);
            return Mathf.Clamp01(raw / Mathf.Max(reference, 0.0001f));
        }

        public void Reset() { Unsubscribe(); _lastJamTime = -999f; _lastJamAmount = 0f; }

        private void Unsubscribe()
        {
            if (_sub != null) { try { _sub.onJam -= OnJam; } catch { } _sub = null; }
        }
    }
}
