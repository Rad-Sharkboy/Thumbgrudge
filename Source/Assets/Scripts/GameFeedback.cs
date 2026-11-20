using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HandsOff
{
    internal sealed class GameFeedback : IDisposable
    {
        internal enum Option { Music, Sound, Vibration }

        private const int SampleRate = 22050;
        private static readonly string[] Keys = { "Thumbgrudge.Music", "Thumbgrudge.Sound", "Thumbgrudge.Vibration" };
        private readonly bool[] enabled = new bool[Keys.Length];
        private readonly AudioSource[] voices = new AudioSource[4];
        private readonly AudioClip[] cues = new AudioClip[6];
        private readonly System.Random random = new System.Random();
        private readonly AudioSource music;
        private readonly AudioClip musicClip;
        private int nextVoice;
        private float musicLevel = 0.24f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly int[] PulseLengths = { 26, 24, 36, 29, 62, 92 };
        private static readonly int[] PulseAmplitudes = { 78, 67, 108, 88, 164, 202 };
        private readonly AndroidJavaObject[] pulses = new AndroidJavaObject[PulseLengths.Length];
        private AndroidJavaObject vibrator;
        private bool checkedMotor;
        private float lastPulseTime = float.NegativeInfinity;
#endif

        public GameFeedback(GameObject owner)
        {
            for (int i = 0; i < Keys.Length; i++) enabled[i] = PlayerPrefs.GetInt(Keys[i], 1) != 0;
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = owner.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.volume = 0.56f;
                voices[i] = voice;
            }
            cues[(int)GameView.Sound.Push] = Synthesize("Push pop", 0.084f, 315f, 91f, 0.12f, 0.35f);
            cues[(int)GameView.Sound.Bank] = Synthesize("Wall boing", 0.15f, 195f, 84f, 0.02f, 0.40f);
            cues[(int)GameView.Sound.Pickup] = Synthesize("Pickup chime", 0.19f, 590f, 720f, 0f, 0.33f);
            cues[(int)GameView.Sound.Nerve] = Synthesize("Nerve tick", 0.076f, 1180f, 810f, 0.035f, 0.26f);
            cues[(int)GameView.Sound.Shield] = Synthesize("Shield pop", 0.19f, 912f, 263f, 0.33f, 0.38f);
            cues[(int)GameView.Sound.Shatter] = Synthesize("Splat and shatter", 0.31f, 255f, 45f, 0.57f, 0.47f);

            music = owner.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.volume = 0f;
            musicClip = ComposeLoop();
            music.clip = musicClip;
            if (musicClip != null && IsOn(Option.Music)) music.Play();
        }

        public bool IsOn(Option option) => enabled[(int)option];

        public bool HasVibration
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!checkedMotor) FindVibrator();
                return vibrator != null;
#else
                return false;
#endif
            }
        }

        public void Toggle(Option option)
        {
            if (option == Option.Vibration && !HasVibration) return;
            int i = (int)option;
            enabled[i] = !enabled[i];
            PlayerPrefs.SetInt(Keys[i], enabled[i] ? 1 : 0);
            PlayerPrefs.Save();
            if (option == Option.Music && enabled[i] && music.clip != null && !music.isPlaying) music.Play();
            if (option == Option.Sound && !enabled[i])
                foreach (var voice in voices) voice.Stop();
            if (option == Option.Vibration && enabled[i]) Vibrate(GameView.Sound.Pickup);
        }

        public void SetMusicLevel(float level) => musicLevel = level;

        public void Tick(float dt)
        {
            if (music == null) return;
            bool play = IsOn(Option.Music);
            music.volume = Mathf.MoveTowards(music.volume, play ? musicLevel : 0f, dt * 1.8f);
            if (!play && music.isPlaying && music.volume <= 0f) music.Stop();
        }

        public void Play(GameView.Sound sound, float pitch)
        {
            Vibrate(sound);
            var clip = cues[(int)sound];
            if (!IsOn(Option.Sound) || clip == null) return;
            for (int i = 0; i < voices.Length; i++)
            {
                int index = (nextVoice + i) % voices.Length;
                if (voices[index] != null && !voices[index].isPlaying)
                {
                    nextVoice = index;
                    break;
                }
            }
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            if (voice == null) return;
            voice.pitch = pitch * Mathf.Lerp(0.93f, 1.07f, (float)random.NextDouble());
            voice.clip = clip;
            voice.Play();
        }

        private void Vibrate(GameView.Sound cue)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsOn(Option.Vibration) || !HasVibration) return;
            float now = Time.unscaledTime;
            if (now - lastPulseTime < 0.055f && cue != GameView.Sound.Shield && cue != GameView.Sound.Shatter) return;
            try
            {
                vibrator.Call("vibrate", pulses[(int)cue]);
                lastPulseTime = now;
            }
            catch (AndroidJavaException)
            {
                ReleaseVibration();
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void FindVibrator()
        {
            var activity = AndroidApplication.currentActivity;
            if (activity == null) return;
            checkedMotor = true;
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") >= 31)
                {
                    using var manager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager");
                    vibrator = manager?.Call<AndroidJavaObject>("getDefaultVibrator");
                }
                vibrator ??= activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                {
                    ReleaseVibration();
                    return;
                }

                bool adjustable = vibrator.Call<bool>("hasAmplitudeControl");
                using var effect = new AndroidJavaClass("android.os.VibrationEffect");
                for (int i = 0; i < pulses.Length; i++)
                    pulses[i] = effect.CallStatic<AndroidJavaObject>("createOneShot",
                        (long)PulseLengths[i], adjustable ? PulseAmplitudes[i] : -1);
            }
            catch (AndroidJavaException)
            {
                ReleaseVibration();
            }
        }

        private void ReleaseVibration()
        {
            foreach (var pulse in pulses) pulse?.Dispose();
            Array.Clear(pulses, 0, pulses.Length);
            vibrator?.Dispose();
            vibrator = null;
        }
#endif

        private AudioClip Synthesize(string name, float duration, float from, float to, float grain, float gain)
        {
            var samples = new float[Mathf.CeilToInt(duration * SampleRate)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / samples.Length;
                float phase = 2f * Mathf.PI * duration * (from * t + (to - from) * t * t * 0.5f);
                float tone = 0.76f * Mathf.Sin(phase) + 0.24f * Mathf.Sin(phase * 2.01f);
                float noise = (float)random.NextDouble() * 2f - 1f;
                float envelope = Mathf.Min(1f, t * 36f) * (1f - t) * (1f - t);
                samples[i] = Mathf.Lerp(tone, noise, grain * (1f - t)) * envelope * gain;
            }
            return MakeClip(name, samples);
        }

        private static AudioClip ComposeLoop()
        {
            // Swing eighth notes over a toy-marimba melody, soft bass, and brushed percussion.
            const float beat = 60f / 126f;
            int[,] melody =
            {
                { 72, 0, 76, 79, 0, 76, 74, 0 },
                { 72, 0, 69, 72, 76, 0, 0, 0 },
                { 77, 0, 76, 72, 74, 0, 72, 0 },
                { 71, 74, 0, 79, 76, 0, 74, 0 },
                { 72, 76, 79, 0, 81, 0, 79, 76 },
                { 72, 0, 76, 72, 69, 0, 0, 72 },
                { 77, 76, 0, 72, 74, 0, 77, 0 },
                { 79, 0, 74, 71, 74, 0, 0, 0 }
            };
            int[] roots = { 48, 45, 41, 43, 48, 45, 41, 43 };
            var samples = new float[Mathf.RoundToInt(roots.Length * 4f * beat * SampleRate)];
            var percussion = new System.Random(412);

            for (int bar = 0; bar < roots.Length; bar++)
            {
                for (int eighth = 0; eighth < 8; eighth++)
                {
                    float time = (bar * 4f + eighth / 2 + (eighth % 2 == 0 ? 0f : 0.57f)) * beat;
                    int note = melody[bar, eighth];
                    if (note != 0) MixNote(samples, time, note, 0.204f, 0.22f, 0.24f);
                    if (eighth % 4 == 0) MixNote(samples, time, roots[bar], 0.31f, 0.19f, 0.34f);
                    if (eighth == 3 || eighth == 7) MixNote(samples, time, roots[bar] + 7, 0.16f, 0.10f, 0.31f);
                    if (eighth % 2 != 0) MixBrush(samples, time, 0.034f, 0.035f, percussion);
                    if (eighth == 2 || eighth == 6) MixBrush(samples, time, 0.069f, 0.103f, percussion);
                }
            }
            return MakeClip("Thumbgrudge - Toybox Shuffle", samples);
        }

        private static void MixNote(float[] samples, float time, int note, float duration, float gain, float overtone)
        {
            float step = 2f * Mathf.PI * 440f * Mathf.Pow(2f, (note - 69) / 12f) / SampleRate;
            int start = Mathf.RoundToInt(time * SampleRate);
            int length = Mathf.RoundToInt(duration * SampleRate);
            for (int i = 0; i < length && start + i < samples.Length; i++)
            {
                float t = (float)i / length;
                float fade = Mathf.Min(1f, i / 110f) * (1f - t) * (1f - t);
                float phase = step * i;
                float wave = (1f - overtone) * Mathf.Sin(phase) + overtone * Mathf.Sin(phase * 2.01f) * (1f - t);
                samples[start + i] += wave * fade * gain;
            }
        }

        private static void MixBrush(float[] samples, float time, float duration, float gain, System.Random noise)
        {
            int start = Mathf.RoundToInt(time * SampleRate);
            int length = Mathf.RoundToInt(duration * SampleRate);
            float previous = 0f;
            for (int i = 0; i < length && start + i < samples.Length; i++)
            {
                float raw = (float)noise.NextDouble() * 2f - 1f;
                float falloff = 1f - (float)i / length;
                samples[start + i] += (raw - previous) * gain * Mathf.Min(1f, i / 80f) * falloff * falloff;
                previous = raw;
            }
        }

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            if (clip != null) clip.SetData(samples, 0);
            return clip;
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ReleaseVibration();
#endif
            if (musicClip != null) UnityEngine.Object.Destroy(musicClip);
            foreach (var clip in cues)
                if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
