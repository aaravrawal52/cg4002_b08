using System;
using UnityEngine;

namespace ARVisualizer
{
    public enum AppSound
    {
        Tick, Up, Down, Start, Pause, Reset, Look, Alarm,
        Reveal, Hide, Next, Previous,
        Slide, Blocked, Shuffle, Solved
    }

    /// <summary>
    /// Short interface sounds for the screen apps. They are synthesised on first use, so the project
    /// carries no audio files; change a recipe in <see cref="Samples"/> to change how a sound feels.
    /// </summary>
    public static class AppSounds
    {
        public const int SampleRate = 44100;
        public static readonly int Count = Enum.GetValues(typeof(AppSound)).Length;

        /// <summary>Scales every app sound; 0 silences them. Set from AR Visualizer App > App Sound Volume.</summary>
        public static float MasterVolume { get; set; } = 0.7f;

        static readonly AudioClip[] clips = new AudioClip[Count];

        public static AudioClip Clip(AppSound sound)
        {
            int index = (int)sound;
            if (clips[index] != null) return clips[index];
            var samples = Samples(sound);
            var clip = AudioClip.Create("App sound / " + sound, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clips[index] = clip;
        }

        // Musical pitches used below, in hertz.
        const float C5 = 523.25f, E5 = 659.25f, G5 = 783.99f, C6 = 1046.5f, G6 = 1567.98f;

        /// <summary>One mono waveform per sound, between -1 and 1 at <see cref="SampleRate"/>.</summary>
        public static float[] Samples(AppSound sound)
        {
            switch (sound)
            {
                // Timer
                case AppSound.Tick: return new Mix(0.05f).Tone(0, 0.045f, 1900, 1900, 0.6f, 5, 0).Data;
                case AppSound.Up: return new Mix(0.09f).Tone(0, 0.085f, 740, 880, 0.4f, 6).Data;
                case AppSound.Down: return new Mix(0.09f).Tone(0, 0.085f, 660, 520, 0.4f, 6).Data;
                case AppSound.Start: return new Mix(0.27f).Tone(0, 0.1f, C5, C5, 0.36f, 5).Tone(0.085f, 0.18f, G5, G5, 0.4f, 5).Data;
                case AppSound.Pause: return new Mix(0.27f).Tone(0, 0.1f, G5, G5, 0.36f, 5).Tone(0.085f, 0.18f, C5, C5, 0.4f, 5).Data;
                case AppSound.Reset: return new Mix(0.24f).Tone(0, 0.23f, 620, 260, 0.38f, 4).Data;
                case AppSound.Look: return new Mix(0.26f).Swish(0, 0.25f, 600, 5000, 0.28f, 11).Tone(0.02f, 0.2f, 392, 587, 0.16f, 3).Data;
                case AppSound.Alarm:
                {
                    var mix = new Mix(0.56f);
                    for (int i = 0; i < 3; i++) mix.Tone(i * 0.18f, 0.12f, G6, G6, 0.42f, 1.5f, 0.35f);
                    return mix.Data;
                }

                // Flashcards
                case AppSound.Reveal: return new Mix(0.2f).Click(0, 0.06f, 5500, 2500, 0.22f, 21).Tone(0.01f, 0.18f, 587, 880, 0.3f, 5).Data;
                case AppSound.Hide: return new Mix(0.2f).Click(0, 0.06f, 4000, 1800, 0.2f, 22).Tone(0.01f, 0.17f, 880, 587, 0.26f, 6).Data;
                case AppSound.Next: return new Mix(0.2f).Swish(0, 0.19f, 900, 6500, 0.3f, 31).Data;
                case AppSound.Previous: return new Mix(0.2f).Swish(0, 0.19f, 6500, 900, 0.3f, 32).Data;

                // Slider puzzle
                case AppSound.Slide: return new Mix(0.09f).Click(0, 0.02f, 3000, 1500, 0.45f, 41).Tone(0.004f, 0.075f, 320, 230, 0.55f, 8, 0.4f).Data;
                case AppSound.Blocked: return new Mix(0.14f).Click(0, 0.03f, 700, 300, 0.35f, 42).Tone(0, 0.13f, 220, 150, 0.5f, 6, 0.5f).Data;
                case AppSound.Shuffle:
                {
                    var mix = new Mix(0.48f);
                    for (int i = 0; i < 7; i++)
                    {
                        float start = i * 0.055f, pitch = 260 + i * 37 % 5 * 30;
                        mix.Click(start, 0.015f, 3000, 1500, 0.32f, 50 + i).Tone(start + 0.004f, 0.06f, pitch, pitch * 0.75f, 0.5f, 8, 0.4f);
                    }
                    return mix.Data;
                }
                default: // Solved
                    return new Mix(0.95f).Tone(0, 0.16f, C5, C5, 0.3f, 4).Tone(0.1f, 0.16f, E5, E5, 0.3f, 4)
                        .Tone(0.2f, 0.16f, G5, G5, 0.3f, 4).Tone(0.3f, 0.62f, C6, C6, 0.34f, 4).Tone(0.3f, 0.62f, G6, G6, 0.1f, 5, 0).Data;
            }
        }

        /// <summary>Adds simple voices into one buffer. Times are in seconds, frequencies in hertz.</summary>
        sealed class Mix
        {
            public float[] Data
            {
                get
                {
                    for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1, 1);
                    return data;
                }
            }
            readonly float[] data;
            const float Attack = 0.004f * SampleRate, Release = 0.006f * SampleRate;

            public Mix(float seconds) { data = new float[Mathf.CeilToInt(seconds * SampleRate)]; }

            /// <summary>A note gliding between two pitches: quick attack, exponential tail, optional octave overtone.</summary>
            public Mix Tone(float start, float length, float from, float to, float gain, float decay, float overtone = 0.2f)
            {
                int first = (int)(start * SampleRate), count = (int)(length * SampleRate);
                double phase = 0;
                for (int i = 0; i < count && first + i < data.Length; i++)
                {
                    float t = (float)i / count;
                    phase += 2 * Math.PI * Mathf.Lerp(from, to, t) / SampleRate;
                    float envelope = Mathf.Min(1, i / Attack) * Mathf.Exp(-decay * t) * Mathf.Min(1, (count - i) / Release);
                    data[first + i] += gain * envelope * (float)(Math.Sin(phase) + overtone * Math.Sin(2 * phase));
                }
                return this;
            }

            /// <summary>Rushing air: noise that swells and fades while its brightness sweeps.</summary>
            public Mix Swish(float start, float length, float from, float to, float gain, int seed) =>
                Noise(start, length, from, to, gain, seed, true);

            /// <summary>A short tap: noise that starts at once and dies away.</summary>
            public Mix Click(float start, float length, float from, float to, float gain, int seed) =>
                Noise(start, length, from, to, gain, seed, false);

            Mix Noise(float start, float length, float from, float to, float gain, int seed, bool swell)
            {
                int first = (int)(start * SampleRate), count = (int)(length * SampleRate);
                var random = new System.Random(seed);
                float low = 0;
                for (int i = 0; i < count && first + i < data.Length; i++)
                {
                    float t = (float)i / count;
                    // One-pole low-pass whose cut-off sweeps across the sound.
                    float k = 1 - Mathf.Exp(-2 * Mathf.PI * Mathf.Lerp(from, to, t) / SampleRate);
                    low += k * ((float)random.NextDouble() * 2 - 1 - low);
                    float bell = Mathf.Sin(Mathf.PI * t);
                    float envelope = swell ? bell * bell
                        : Mathf.Min(1, i / (Attack * 0.25f)) * Mathf.Exp(-7 * t) * Mathf.Min(1, (count - i) / Release);
                    data[first + i] += gain * envelope * low * 2;
                }
                return this;
            }
        }
    }
}
