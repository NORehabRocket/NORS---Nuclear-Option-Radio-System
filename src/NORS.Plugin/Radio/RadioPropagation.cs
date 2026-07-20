using NORS.Common;
using UnityEngine;

namespace NORS.Plugin.Comms
{
    internal struct Propagation
    {
        public bool Audible;
        public float Volume;   // 0..1 signal level
        public float Quality;  // 0..1 clarity (drives static mix)
        public float DistanceKm;
        public bool TerrainBlocked;
    }

    /// <summary>
    /// Models whether and how well a transmission reaches the local receiver, using:
    ///   - free-space range falloff capped by the equipment's max range,
    ///   - the radio horizon 4.12*(sqrt(h_tx)+sqrt(h_rx)) km (so altitude dramatically extends reach),
    ///   - terrain line-of-sight via a physics raycast on the game's terrain layers,
    ///   - AM vs FM behaviour (AM longer but more terrain-sensitive; FM tolerates some blockage),
    ///   - modulation mismatch garble.
    /// </summary>
    internal static class RadioPropagation
    {
        public static Propagation Compute(
            bool selfHasPos, Vector3 selfWorld, float selfAltM,
            GlobalPosition txGlobal, Modulation txMod, bool modMatch)
        {
            Propagation p = default;

            // A listener with no body in the world (sitting in the map/spectating) is treated as a
            // ground station with perfect reception.
            if (!selfHasPos)
            {
                p.Audible = true; p.Volume = 1f; p.Quality = modMatch ? 1f : 0.15f;
                return p;
            }

            Vector3 txWorld = txGlobal.ToLocalPosition();
            float dist = Vector3.Distance(selfWorld, txWorld);
            float distKm = dist / 1000f;
            p.DistanceKm = distKm;

            float txAlt = txGlobal.y;
            float horizonKm = 4.12f * (Mathf.Sqrt(Mathf.Max(txAlt, 1f)) + Mathf.Sqrt(Mathf.Max(selfAltM, 1f)))
                              * NorsConfig.HorizonFactor.Value;

            bool fm = txMod == Modulation.FM;
            float baseRangeKm = fm ? NorsConfig.FmRangeKm.Value : NorsConfig.AmRangeKm.Value;
            float minKm = NorsConfig.MinGroundRangeKm.Value;

            // FM diffracts a little past the geometric horizon.
            float effectiveKm = Mathf.Clamp(horizonKm * (fm ? 1.25f : 1.0f), minKm, baseRangeKm);

            float r = effectiveKm > 0f ? distKm / effectiveKm : 999f;

            float volume, quality;
            if (r <= 1f)
            {
                volume = Mathf.Clamp01(1f - r);
                quality = Mathf.Clamp01(1f - r * r);
            }
            else if (r <= 1.3f)
            {
                // Faint fringe past max range.
                volume = (1.3f - r) / 0.3f * 0.15f;
                quality = 0.1f;
            }
            else
            {
                p.Audible = false; p.Volume = 0f; p.Quality = 0f;
                return p;
            }

            // Terrain line-of-sight.
            if (NorsConfig.TerrainLOS.Value)
            {
                Vector3 up = Vector3.up * 3f; // lift endpoints off the surface to avoid grazing self-hits
                if (Physics.Linecast(txWorld + up, selfWorld + up, out _, NorsConfig.TerrainMask.Value))
                {
                    p.TerrainBlocked = true;
                    if (fm) { volume *= 0.55f; quality = Mathf.Min(quality, 0.40f); }
                    else { volume *= 0.30f; quality = Mathf.Min(quality, 0.18f); }
                }
            }

            // Wrong modulation on the same frequency = unintelligible.
            if (!modMatch)
            {
                volume *= 0.5f;
                quality = Mathf.Min(quality, 0.12f);
            }

            p.Volume = Mathf.Clamp01(volume);
            p.Quality = Mathf.Clamp01(quality);
            p.Audible = p.Volume > 0.02f;
            return p;
        }
    }
}
