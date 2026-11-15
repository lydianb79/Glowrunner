using System;
using UnityEngine;

namespace Glowrunner
{
    /// <summary>Tiny synthesizer: all sound effects are generated at startup, no audio files needed.</summary>
    public class Sfx : MonoBehaviour
    {
        const int Rate = 44100;

        AudioSource source;
        AudioClip sparkA, sparkB, core, zap, crash, clear, start, extraLife;
        bool alternate;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = 0.45f;

            sparkA = Sweep("SparkA", 0.05f, 1320f, 1580f, 0.18f, 0.4f);
            sparkB = Sweep("SparkB", 0.05f, 1180f, 1400f, 0.18f, 0.4f);
            core = Synth("Core", 0.45f, x => Mathf.Lerp(220f, 880f, x) * (1f + 0.05f * Mathf.Sin(x * 60f)), 0.35f, 0.2f);
            zap = Sweep("Zap", 0.32f, 1800f, 300f, 0.35f, 0.5f);
            crash = Synth("Crash", 1.3f, x => Mathf.Lerp(700f, 70f, x) * (1f + 0.12f * Mathf.Sin(x * 90f)), 0.4f, 0.6f);
            clear = Notes("Clear", 0.11f, 0.3f, 659f, 784f, 988f, 1319f, 1568f);
            start = Notes("Start", 0.14f, 0.3f, 440f, 554f, 659f, 554f, 880f);
            extraLife = Notes("ExtraLife", 0.08f, 0.3f, 1047f, 1319f, 1568f, 2093f);
        }

        public void Spark()
        {
            alternate = !alternate;
            source.PlayOneShot(alternate ? sparkA : sparkB);
        }

        public void Core() => source.PlayOneShot(core);
        public void Zap() => source.PlayOneShot(zap);
        public void Crash() => source.PlayOneShot(crash);
        public void Clear() => source.PlayOneShot(clear);
        public void Jingle() => source.PlayOneShot(start);
        public void ExtraLife() => source.PlayOneShot(extraLife);

        static AudioClip Sweep(string name, float dur, float from, float to, float vol, float square) =>
            Synth(name, dur, x => Mathf.Lerp(from, to, x), vol, square);

        static AudioClip Notes(string name, float noteDur, float vol, params float[] freqs)
        {
            float total = noteDur * freqs.Length;
            return Synth(name, total, x => freqs[Mathf.Min(freqs.Length - 1, (int)(x * freqs.Length))], vol, 0.35f);
        }

        /// <param name="freqAt">Frequency as a function of normalized time 0..1.</param>
        /// <param name="square">Blend between sine (0) and square (1).</param>
        static AudioClip Synth(string name, float duration, Func<float, float> freqAt, float volume, float square)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float x = (float)i / n;
                ph += freqAt(x) / Rate;
                float s = Mathf.Sin((float)(ph * 2.0 * Math.PI));
                float sq = s >= 0f ? 1f : -1f;
                float wave = Mathf.Lerp(s, sq, square);
                float attack = Mathf.Clamp01(i / (0.004f * Rate));
                float release = Mathf.Clamp01((n - i) / (0.03f * Rate));
                data[i] = wave * volume * attack * release;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
