using System;
using NORS.Common;
using UnityEngine;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Captures the microphone via Unity's <see cref="Microphone"/>, resamples to 48 kHz if needed,
    /// chops into 20 ms frames, Opus-encodes them, and hands each encoded frame to <see cref="OnFrame"/>.
    /// Only runs while push-to-talk is held.
    /// </summary>
    internal sealed class VoiceCapture
    {
        /// <summary>(encodedData, encodedLen, sequence) — data buffer is reused, consume synchronously.</summary>
        public Action<byte[], int, uint> OnFrame;

        public bool Capturing { get; private set; }
        public bool MicAvailable => Microphone.devices != null && Microphone.devices.Length > 0;
        public float LastLevel { get; private set; }
        public int FramesEncoded { get; private set; }
        public string DeviceLabel { get; private set; } = "(idle)";

        private float _startTime;
        private bool _warnedNoAdvance;
        private bool _transmitting;

        private RawEncoderStream _enc;
        private AudioClip _micClip;
        private string _device;
        private int _micRate;
        private int _clipSamples;
        private int _lastMicPos;
        private uint _seq;

        private float[] _readBuf;   // raw mic samples (mic rate), sized to the read count
        private float[] _resBuf;    // resampled to 48 kHz
        private float[] _accum;     // 48 kHz accumulator
        private int _accumCount;
        private readonly float[] _frame = new float[NorsProtocol.FrameSamples];

        /// <summary>True only while actually transmitting (mic open AND PTT held).</summary>
        public bool Transmitting => Capturing && _transmitting;

        /// <summary>
        /// Sets whether we're transmitting. The mic is opened on the first key-down and then kept
        /// running — gating transmission here instead of calling Microphone.Start/End each PTT avoids
        /// the big frame hitch that happened every time you keyed up.
        /// </summary>
        public void SetActive(bool active)
        {
            _transmitting = active;
            if (active && !Capturing) StartMic();
        }

        /// <summary>Fully stop and release the mic (call when leaving the mission).</summary>
        public void Stop()
        {
            _transmitting = false;
            if (Capturing) StopMic();
        }

        private void StartMic()
        {
            if (!MicAvailable)
            {
                NorsPlugin.Log.LogWarning("NORS: no microphone device available.");
                return;
            }

            _device = ResolveDevice();
            int request = NorsProtocol.SampleRate;
            try
            {
                Microphone.GetDeviceCaps(_device, out int min, out int max);
                if (max > 0) request = Mathf.Clamp(request, min == 0 ? min + 1 : min, max);
            }
            catch { }

            try { _micClip = Microphone.Start(_device, true, 1, request); }
            catch (Exception e)
            {
                NorsPlugin.Log.LogError("NORS: Microphone.Start failed: " + e.Message);
                _micClip = null;
                return;
            }
            if (_micClip == null) return;

            _micRate = _micClip.frequency;
            _clipSamples = _micClip.samples;
            _lastMicPos = 0;
            _accumCount = 0;

            int up = Mathf.Max(1, (NorsProtocol.SampleRate / Mathf.Max(_micRate, 1)) + 2);
            _resBuf = new float[_clipSamples * up];
            _accum = new float[NorsProtocol.SampleRate]; // up to 1s of slack

            if (_enc == null) _enc = new RawEncoderStream();
            Capturing = true;
            _startTime = Time.unscaledTime;
            _warnedNoAdvance = false;
            DeviceLabel = $"{(_device ?? "default")}@{_micRate}Hz";
            NorsPlugin.Log.LogInfo($"NORS: mic '{(_device ?? "default")}' started at {_micRate} Hz.");
        }

        private void StopMic()
        {
            Capturing = false;
            LastLevel = 0f;
            try { Microphone.End(_device); } catch { }
            _micClip = null;
            _accumCount = 0;
        }

        public void Tick(float micGain)
        {
            if (!Capturing || _micClip == null) return;

            int pos = Microphone.GetPosition(_device);
            if (pos < 0) return;

            // Mic stays open between transmissions; while not keyed, just discard incoming samples so
            // there's no backlog to flush (and no stale audio) when PTT is next pressed.
            if (!_transmitting)
            {
                _lastMicPos = pos;
                _accumCount = 0;
                LastLevel = 0f;
                return;
            }

            int avail = pos - _lastMicPos;
            if (avail < 0) avail += _clipSamples;
            if (avail <= 0)
            {
                // The mic position never advancing is the classic "no permission / wrong device" symptom.
                if (!_warnedNoAdvance && FramesEncoded == 0 && Time.unscaledTime - _startTime > 1.5f)
                {
                    _warnedNoAdvance = true;
                    NorsPlugin.Log.LogWarning("NORS: microphone is not producing samples. Check Windows mic permissions " +
                                              "(Settings > Privacy > Microphone, allow desktop apps) and the MicDevice config.");
                }
                return;
            }
            if (avail > _clipSamples - 1) avail = _clipSamples - 1; // fell behind > 1s; skip ahead

            // GetData reads exactly buffer.Length samples from the offset, so the buffer must match
            // the read count. avail is roughly steady frame-to-frame, so this rarely reallocates.
            if (_readBuf == null || _readBuf.Length != avail) _readBuf = new float[avail];

            if (_lastMicPos + avail <= _clipSamples)
            {
                _micClip.GetData(_readBuf, _lastMicPos);
            }
            else
            {
                int first = _clipSamples - _lastMicPos;
                int second = avail - first;
                var a = new float[first];
                _micClip.GetData(a, _lastMicPos);
                var b = new float[second];
                _micClip.GetData(b, 0);
                Array.Copy(a, 0, _readBuf, 0, first);
                Array.Copy(b, 0, _readBuf, first, second);
            }
            _lastMicPos = pos;

            int resN = Resample(_readBuf, avail, _micRate, _resBuf, NorsProtocol.SampleRate);

            // Apply gain, track level, append to accumulator.
            float peak = 0f;
            for (int i = 0; i < resN && _accumCount < _accum.Length; i++)
            {
                float s = _resBuf[i] * micGain;
                if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                float mag = s < 0 ? -s : s;
                if (mag > peak) peak = mag;
                _accum[_accumCount++] = s;
            }
            LastLevel = peak;

            // Emit full frames.
            while (_accumCount >= NorsProtocol.FrameSamples)
            {
                Array.Copy(_accum, 0, _frame, 0, NorsProtocol.FrameSamples);
                int rem = _accumCount - NorsProtocol.FrameSamples;
                if (rem > 0) Array.Copy(_accum, NorsProtocol.FrameSamples, _accum, 0, rem);
                _accumCount = rem;

                int len = _enc.Encode(_frame, out byte[] data);
                if (len > 0) { FramesEncoded++; OnFrame?.Invoke(data, len, _seq++); }
            }
        }

        private string ResolveDevice()
        {
            string cfg = NorsConfig.MicDevice.Value;
            if (string.IsNullOrEmpty(cfg)) return null; // default device
            foreach (var d in Microphone.devices)
                if (d == cfg) return d;
            NorsPlugin.Log.LogWarning($"NORS: mic '{cfg}' not found, using default.");
            return null;
        }

        private static int Resample(float[] src, int srcLen, int srcRate, float[] dst, int dstRate)
        {
            if (srcRate == dstRate)
            {
                Array.Copy(src, dst, srcLen);
                return srcLen;
            }
            double ratio = (double)dstRate / srcRate;
            int outLen = (int)(srcLen * ratio);
            if (outLen > dst.Length) outLen = dst.Length;
            for (int i = 0; i < outLen; i++)
            {
                double s = i / ratio;
                int i0 = (int)s;
                double f = s - i0;
                int i1 = i0 + 1 < srcLen ? i0 + 1 : srcLen - 1;
                dst[i] = (float)(src[i0] * (1.0 - f) + src[i1] * f);
            }
            return outLen;
        }
    }
}
