using System;
using System.Collections.Generic;
using UnityEngine;

namespace NORS.Plugin.Game
{
    /// <summary>An airborne radio-relay node: an aircraft carrying a Radome, flying high enough to relay.</summary>
    internal struct RelayNode
    {
        public GlobalPosition Global;
        public Vector3 World;
        public float AltM;
        public int FactionId;
    }

    /// <summary>
    /// Tracks radio relays. Airborne: aircraft carrying a Radome (e.g. the EW-1 Medusa AEW) flying
    /// above the relay altitude — they let transmissions hop over terrain / past normal range by being
    /// high enough to see both ends. Ground/naval: radar stations, carriers, and airbase towers — low,
    /// so they only cover their local horizon (around-base / fleet comms). Scanned periodically, not
    /// per voice frame, since enumerating units + reading loadouts isn't free.
    /// </summary>
    internal sealed class RelaySense
    {
        private const float GroundMastMeters = 25f;   // antenna mast above the station/ship deck
        private const float TowerMastMeters = 30f;    // airbase ATC tower antenna

        private readonly List<RelayNode> _nodes = new List<RelayNode>();
        private float _nextScan;

        public IReadOnlyList<RelayNode> Nodes => _nodes;

        public void Tick(float now)
        {
            bool air = NorsConfig.RelayViaRadome.Value;
            bool ground = NorsConfig.RelayGroundStations.Value;
            if (!air && !ground) { _nodes.Clear(); return; }
            if (now < _nextScan) return;
            _nextScan = now + 2f;

            _nodes.Clear();
            float minAlt = NorsConfig.RelayMinAltitudeMeters.Value;
            try
            {
                var all = UnitRegistry.allUnits;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.disabled) continue;

                        if (u is Aircraft ac)
                        {
                            if (!air || !HasRadome(ac)) continue;
                            Vector3 w = ac.transform.position;
                            GlobalPosition g = w.ToGlobalPosition();
                            if (g.y < minAlt) continue;   // too low to see over terrain → no relay
                            Add(u, w, g, g.y);
                        }
                        else if (ground && u.radar != null)
                        {
                            // Radar stations, carriers, SAM search radars: local-area ground relays.
                            Vector3 w = u.transform.position;
                            GlobalPosition g = w.ToGlobalPosition();
                            Add(u, w, g, g.y + GroundMastMeters);
                        }
                    }
                }

                if (ground)
                {
                    // Airbase (ATC tower) relays — airbases aren't units, so enumerate them directly.
                    var bases = UnityEngine.Object.FindObjectsOfType<Airbase>();
                    for (int i = 0; i < bases.Length; i++)
                    {
                        var ab = bases[i];
                        if (ab == null || ab.CurrentHQ == null || ab.center == null) continue;
                        Vector3 w = ab.center.position;
                        GlobalPosition g = w.ToGlobalPosition();
                        _nodes.Add(new RelayNode
                        {
                            Global = g, World = w, AltM = g.y + TowerMastMeters,
                            FactionId = (int)ab.CurrentHQ.NetId
                        });
                    }
                }
            }
            catch { }
        }

        private void Add(Unit u, Vector3 w, GlobalPosition g, float altM)
        {
            int fac = 0;
            try { if (u.NetworkHQ != null) fac = (int)u.NetworkHQ.NetId; } catch { }
            _nodes.Add(new RelayNode { Global = g, World = w, AltM = altM, FactionId = fac });
        }

        private static bool HasRadome(Aircraft ac)
        {
            try
            {
                var stations = ac.weaponStations;
                if (stations == null) return false;
                for (int i = 0; i < stations.Count; i++)
                {
                    var info = stations[i] != null ? stations[i].WeaponInfo : null;
                    if (info != null && (NameHas(info.weaponName) || NameHas(info.shortName))) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool NameHas(string s) =>
            !string.IsNullOrEmpty(s) && s.IndexOf("radome", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
