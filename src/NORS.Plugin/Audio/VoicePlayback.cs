using System.Collections.Generic;
using NORS.Common;
using NORS.Plugin.Comms;
using UnityEngine;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Plays incoming radio voice. Each remote talker gets a dedicated streaming AudioSource fed by
    /// a jitter ring buffer; decoded frames are run through <see cref="RadioFx"/> with the volume and
    /// clarity produced by the propagation model. Default playback is in-headset (2D); enabling
    /// OpenAir3D positions the voice in world space.
    /// </summary>
    internal sealed class VoicePlayback
    {
        private sealed class Talker
        {
            public uint Id;
            public string Name;
            public GameObject Go;
            public AudioSource Source;
            public RawDecoderStream Dec;
            public FloatRingBuffer Ring;
            public RadioFx Fx;
            public readonly float[] DecodeBuf = new float[NorsProtocol.FrameSamples];
            public float LastActive;
            public Vector3 World;
            public bool OpenAir;
            public float LastVolume;
            public float LastQuality;
            public bool TerrainBlocked;
            public float DistanceKm;
        }

        private readonly Dictionary<uint, Talker> _talkers = new Dictionary<uint, Talker>();
        private readonly List<uint> _cullScratch = new List<uint>();
        private Transform _root;

        public int ActiveTalkers => _talkers.Count;

        public void Init(Transform root) => _root = root;

        /// <summary>Number of currently-receiving talkers, and writes their callsigns into the list.</summary>
        public void GetActive(List<string> outNames)
        {
            outNames.Clear();
            float now = Time.unscaledTime;
            foreach (var t in _talkers.Values)
            {
                if (now - t.LastActive < 0.5f)
                    outNames.Add($"{t.Name}  {(t.TerrainBlocked ? "~" : "")}{(int)(t.LastQuality * 100)}%  {t.DistanceKm:0}km");
            }
        }

        /// <summary>
        /// Sidetone: decode and play one of our OWN outgoing frames locally (talker id 0), so a solo
        /// user can verify the whole mic -> Opus -> decode -> playback chain without a second player.
        /// </summary>
        public void Monitor(byte[] data, int audioLen)
        {
            Talker t = GetOrCreate(0u, false);
            t.Name = "(you)";
            t.LastActive = Time.unscaledTime;
            int n = t.Dec.Decode(data, audioLen, t.DecodeBuf);
            if (n <= 0) return;
            t.Fx.Process(t.DecodeBuf, n, 1f, NorsConfig.ReceiveVolume.Value, NORS.Common.Modulation.AM, NorsConfig.StaticLevel.Value);
            t.Ring.Write(t.DecodeBuf, 0, n);
        }

        public void OnFrame(VoiceHeader v, Propagation p, float radioVolume, string name, bool openAir)
        {
            Talker t = GetOrCreate(v.ClientId, openAir);
            t.Name = string.IsNullOrEmpty(name) ? $"#{v.ClientId}" : name;
            t.LastActive = Time.unscaledTime;
            t.World = new GlobalPosition(v.X, v.Y, v.Z).ToLocalPosition();
            t.LastVolume = p.Volume;
            t.LastQuality = p.Quality;
            t.TerrainBlocked = p.TerrainBlocked;
            t.DistanceKm = p.DistanceKm;

            int n = t.Dec.Decode(v.Audio, v.AudioLen, t.DecodeBuf);
            if (n <= 0) return;

            float gain = p.Volume * radioVolume * NorsConfig.ReceiveVolume.Value;
            t.Fx.Process(t.DecodeBuf, n, p.Quality, gain, v.Mod, NorsConfig.StaticLevel.Value);
            t.Ring.Write(t.DecodeBuf, 0, n);

            if (openAir && t.Go != null) t.Go.transform.position = t.World;
        }

        public void Update()
        {
            if (_talkers.Count == 0) return;
            float now = Time.unscaledTime;
            float cull = NorsConfig.CullSeconds.Value;
            bool openAir = NorsConfig.OpenAir3D.Value;

            _cullScratch.Clear();
            foreach (var kv in _talkers)
            {
                var t = kv.Value;
                if (now - t.LastActive > cull && t.Ring.Count == 0)
                {
                    _cullScratch.Add(kv.Key);
                    continue;
                }
                if (openAir && t.Go != null)
                {
                    t.Go.transform.position = t.World;
                    if (t.Source != null) t.Source.spatialBlend = 1f;
                }
                else if (t.Source != null)
                {
                    t.Source.spatialBlend = 0f;
                }
            }
            foreach (var id in _cullScratch) Destroy(id);
        }

        public void Clear()
        {
            _cullScratch.Clear();
            foreach (var id in _talkers.Keys) _cullScratch.Add(id);
            foreach (var id in _cullScratch) Destroy(id);
            _talkers.Clear();
        }

        private Talker GetOrCreate(uint id, bool openAir)
        {
            if (_talkers.TryGetValue(id, out var t)) return t;

            t = new Talker
            {
                Id = id,
                Dec = new RawDecoderStream(),
                Ring = new FloatRingBuffer(NorsProtocol.SampleRate), // up to 1s of jitter buffer
                Fx = new RadioFx(),
                OpenAir = openAir,
            };

            var go = new GameObject($"NORS_Talker_{id}");
            if (_root != null) go.transform.SetParent(_root, false);
            go.transform.position = Vector3.zero;

            var ring = t.Ring;
            var clip = AudioClip.Create($"NORS_Voice_{id}", NorsProtocol.SampleRate, NorsProtocol.Channels,
                NorsProtocol.SampleRate, true, data => ring.Read(data, 0, data.Length));

            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = openAir ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 50f;
            src.maxDistance = 100000f; // distance attenuation handled by our model, not Unity
            src.bypassReverbZones = true;
            src.dopplerLevel = 0f;
            src.volume = 1f;
            src.Play();

            t.Go = go;
            t.Source = src;
            _talkers[id] = t;
            return t;
        }

        private void Destroy(uint id)
        {
            if (!_talkers.TryGetValue(id, out var t)) return;
            if (t.Source != null) t.Source.Stop();
            if (t.Go != null) Object.Destroy(t.Go);
            _talkers.Remove(id);
        }
    }
}
