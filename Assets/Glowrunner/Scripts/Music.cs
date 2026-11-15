using UnityEngine;

namespace Glowrunner
{
    /// <summary>
    /// Background music, synthesized at startup (no audio files).
    /// Two seamless loops: a calm title theme and a driving in-game track.
    /// The game track speeds up while sentinels are disrupted and is muffled while paused.
    /// </summary>
    public class Music : MonoBehaviour
    {
        public enum Track { None, Title, Game }

        const int Rate = 44100;
        const string MuteKey = "Glowrunner.MusicMuted";

        public float volume = 0.32f;

        AudioSource titleSource, gameSource;
        AudioLowPassFilter gameFilter;
        Track current = Track.None;
        bool disrupted, paused, muted;

        public bool Muted => muted;

        void Awake()
        {
            muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;
            titleSource = MakeSource(BuildTitleTheme());
            var gameGo = new GameObject("Game Music");
            gameGo.transform.SetParent(transform, false);
            gameSource = MakeSource(BuildGameTheme(), gameGo);
            gameFilter = gameGo.AddComponent<AudioLowPassFilter>();
            gameFilter.cutoffFrequency = 22000f;
        }

        AudioSource MakeSource(AudioClip clip, GameObject host = null)
        {
            var s = (host != null ? host : gameObject).AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            s.spatialBlend = 0f;
            return s;
        }

        /// <summary>Switch tracks. The new track starts from the beginning; the old one fades out.</summary>
        public void Play(Track track)
        {
            if (track == current) return;
            current = track;
            var src = Source(track);
            if (src != null) { src.Stop(); src.Play(); }
        }

        public void SetDisrupted(bool on) => disrupted = on;
        public void SetPaused(bool on) => paused = on;

        public void ToggleMute()
        {
            muted = !muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
        }

        AudioSource Source(Track t) => t == Track.Title ? titleSource : t == Track.Game ? gameSource : null;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float master = muted ? 0f : volume;
            // The sustained title pads sound louder than the game track, so they play quieter.
            Fade(titleSource, current == Track.Title ? master * 0.6f : 0f, dt, 0.8f);
            Fade(gameSource, current == Track.Game ? master * (paused ? 0.5f : 1f) : 0f, dt, current == Track.Game ? 3f : 1.5f);

            float pitch = disrupted && !paused ? 1.08f : 1f;
            gameSource.pitch = Mathf.MoveTowards(gameSource.pitch, pitch, dt * 0.6f);
            float cutoff = paused ? 700f : 22000f;
            gameFilter.cutoffFrequency = Mathf.Lerp(gameFilter.cutoffFrequency, cutoff, 1f - Mathf.Exp(-8f * dt));
        }

        static void Fade(AudioSource s, float target, float dt, float speed)
        {
            s.volume = Mathf.MoveTowards(s.volume, target, dt * speed * 0.5f);
            if (s.volume <= 0f && target <= 0f && s.isPlaying) s.Stop();
        }

        // ───────────────────────── Compositions ─────────────────────────

        static AudioClip BuildGameTheme()
        {
            const double bpm = 128.0;
            double beat = 60.0 / bpm, bar = beat * 4;
            var buf = new float[(int)(bar * 8 * Rate)];

            // A minor: Am F G Em | Am F G E
            int[] roots = { 45, 41, 43, 40, 45, 41, 43, 40 };
            bool[] minor = { true, false, false, true, true, false, false, false };

            // Lead melody: (bar, start in eighths, length in eighths, midi note)
            int[,] melody =
            {
                {0,0,2,76},{0,2,1,74},{0,3,1,72},{0,4,2,74},{0,6,2,76},
                {1,0,3,77},{1,3,1,76},{1,4,4,72},
                {2,0,2,74},{2,2,2,71},{2,4,2,67},{2,6,2,71},
                {3,0,2,72},{3,2,2,71},{3,4,4,67},
                {4,0,2,69},{4,2,2,72},{4,4,2,76},{4,6,2,81},
                {5,0,2,79},{5,2,1,77},{5,3,1,76},{5,4,4,77},
                {6,0,2,79},{6,2,2,77},{6,4,2,74},{6,6,2,71},
                {7,0,4,68},{7,4,2,71},{7,6,2,74},
            };

            var rng = new System.Random(7);
            int[] arpPattern = { 0, 1, 2, 3, 4, 3, 2, 1 };

            for (int b = 0; b < 8; b++)
            {
                double t0 = b * bar;
                int r = roots[b];
                int third = minor[b] ? 3 : 4;

                // Pumping bass in eighths with an octave jump.
                for (int e = 0; e < 8; e++)
                {
                    int m = e % 4 == 2 ? r + 12 : r;
                    Note(buf, t0 + e * beat / 2, beat / 2 * 0.75, Midi(m), Wave.Saw, 0.22f, 0.005f, 0.04f, 0.5f, 0.12f);
                }

                // Sparkly sixteenth-note arpeggio.
                int[] tones = { r + 24, r + 24 + third, r + 31, r + 36, r + 36 + third };
                for (int s = 0; s < 16; s++)
                {
                    int m = tones[arpPattern[s % 8]];
                    Note(buf, t0 + s * beat / 4, beat / 4 * 0.9, Midi(m), Wave.Pulse, 0.05f, 0.002f, 0.05f, 0.15f);
                }

                // Drums: kick on every beat, snare on 2 and 4, off-beat hats.
                for (int q = 0; q < 4; q++)
                {
                    Kick(buf, t0 + q * beat, 0.55f);
                    if (q % 2 == 1) Snare(buf, t0 + q * beat, 0.22f, rng);
                    Hat(buf, t0 + q * beat + beat / 2, 0.09f, rng);
                    Hat(buf, t0 + q * beat, 0.035f, rng);
                }
            }

            for (int i = 0; i < melody.GetLength(0); i++)
            {
                double start = melody[i, 0] * bar + melody[i, 1] * beat / 2;
                double len = melody[i, 2] * beat / 2 * 0.92;
                Note(buf, start, len, Midi(melody[i, 3]), Wave.Triangle, 0.16f, 0.01f, 0.08f, 0.7f);
                // A quiet echo an eighth later gives the lead some space.
                Note(buf, start + beat * 0.75, len, Midi(melody[i, 3]), Wave.Triangle, 0.05f, 0.01f, 0.08f, 0.6f);
            }

            return Finish("Glowrunner Game Theme", buf);
        }

        static AudioClip BuildTitleTheme()
        {
            const double bpm = 92.0;
            double beat = 60.0 / bpm, bar = beat * 4;
            var buf = new float[(int)(bar * 8 * Rate)];

            // Am F C G, twice; the second pass adds a higher arpeggio.
            int[] roots = { 45, 41, 48, 43, 45, 41, 48, 43 };
            bool[] minor = { true, false, false, false, true, false, false, false };

            for (int b = 0; b < 8; b++)
            {
                double t0 = b * bar;
                int r = roots[b];
                int third = minor[b] ? 3 : 4;

                // Warm detuned pad.
                foreach (int iv in new[] { 12, 12 + third, 19, 24 })
                {
                    float f = Midi(r + iv);
                    Note(buf, t0, bar, f * 0.997f, Wave.Saw, 0.035f, 0.5f, 0.9f, 0.8f, 0.05f);
                    Note(buf, t0, bar, f * 1.003f, Wave.Saw, 0.035f, 0.5f, 0.9f, 0.8f, 0.05f);
                }

                // Soft bass on beats 1 and 3.
                Note(buf, t0, beat * 1.8, Midi(r), Wave.Sine, 0.25f, 0.02f, 0.3f, 0.6f);
                Note(buf, t0 + beat * 2, beat * 1.8, Midi(r), Wave.Sine, 0.2f, 0.02f, 0.3f, 0.6f);

                // Gentle eighth-note arpeggio.
                int[] tones = { r + 24, r + 24 + third, r + 31, r + 36 };
                int[] pattern = { 0, 2, 1, 3, 2, 1, 3, 2 };
                int lift = b >= 4 ? 12 : 0;
                for (int e = 0; e < 8; e++)
                    Note(buf, t0 + e * beat / 2, beat / 2, Midi(tones[pattern[e]] + lift), Wave.Triangle, 0.07f, 0.005f, 0.25f, 0.3f);
            }

            return Finish("Glowrunner Title Theme", buf);
        }

        // ───────────────────────── Synth helpers ─────────────────────────

        enum Wave { Sine, Triangle, Saw, Pulse }

        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        /// <summary>
        /// Adds a note to the buffer. Tails that run past the end wrap around to the start so loops are seamless.
        /// </summary>
        /// <param name="decayTo">Level reached by the end of the note (1 = sustained, small = plucky).</param>
        /// <param name="lowpass">One-pole low-pass coefficient (1 = off, smaller = darker).</param>
        static void Note(float[] buf, double start, double duration, float freq, Wave wave, float vol,
            float attack, float release, float decayTo, float lowpass = 1f)
        {
            int s0 = (int)(start * Rate);
            int n = Mathf.Max(1, (int)(duration * Rate));
            int rel = Mathf.Max(1, (int)(release * Rate));
            int att = Mathf.Max(1, (int)(attack * Rate));
            double phase = 0, inc = freq / Rate;
            float y = 0f, held = 0f;

            for (int i = 0; i < n + rel; i++)
            {
                float env;
                if (i < n)
                {
                    env = Mathf.Min(1f, (float)i / att) * Mathf.Lerp(1f, decayTo, (float)i / n);
                    held = env;
                }
                else env = held * (1f - (float)(i - n) / rel);

                float p = (float)(phase - System.Math.Floor(phase));
                float x;
                switch (wave)
                {
                    case Wave.Triangle: x = 4f * Mathf.Abs(p - 0.5f) - 1f; break;
                    case Wave.Saw: x = 2f * p - 1f; break;
                    case Wave.Pulse: x = p < 0.25f ? 1f : -0.6f; break;
                    default: x = Mathf.Sin(p * Mathf.PI * 2f); break;
                }
                y += lowpass * (x - y);
                buf[(s0 + i) % buf.Length] += y * env * vol;
                phase += inc;
            }
        }

        static void Kick(float[] buf, double t, float vol)
        {
            int s0 = (int)(t * Rate), n = (int)(0.22 * Rate);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float f = Mathf.Lerp(130f, 42f, Mathf.Sqrt(k));
                phase += f / Rate;
                buf[(s0 + i) % buf.Length] += Mathf.Sin((float)(phase * 2 * System.Math.PI)) * (1f - k) * (1f - k) * vol;
            }
        }

        static void Snare(float[] buf, double t, float vol, System.Random rng)
        {
            int s0 = (int)(t * Rate), n = (int)(0.16 * Rate);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float k = 1f - (float)i / n;
                phase += 185.0 / Rate;
                float tone = Mathf.Sin((float)(phase * 2 * System.Math.PI)) * 0.4f;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                buf[(s0 + i) % buf.Length] += (tone + noise) * k * k * vol;
            }
        }

        static void Hat(float[] buf, double t, float vol, System.Random rng)
        {
            int s0 = (int)(t * Rate), n = (int)(0.045 * Rate);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = 1f - (float)i / n;
                float x = (float)(rng.NextDouble() * 2 - 1);
                buf[(s0 + i) % buf.Length] += (x - prev) * k * k * vol; // crude high-pass
                prev = x;
            }
        }

        static AudioClip Finish(string name, float[] buf)
        {
            float peak = 0f;
            foreach (var v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = peak > 0f ? 0.85f / peak : 1f;
            for (int i = 0; i < buf.Length; i++)
            {
                float x = buf[i] * gain;
                buf[i] = x / (1f + 0.25f * Mathf.Abs(x)) * 1.2f; // gentle soft clip
            }
            var clip = AudioClip.Create(name, buf.Length, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }
    }
}
