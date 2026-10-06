using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// Placeholder sound effects synthesised at load time, so the mod needs no audio files yet.
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = 44100;

        private static AudioClip gunshot, reload, dryFire, hitmarker;

        public static AudioClip Gunshot { get { return gunshot ?? (gunshot = MakeGunshot()); } }
        public static AudioClip Reload { get { return reload ?? (reload = MakeReload()); } }
        public static AudioClip DryFire { get { return dryFire ?? (dryFire = MakeClick("Moon1265_DryFire", 0f, 0.08f, 2500f)); } }
        public static AudioClip Hitmarker { get { return hitmarker ?? (hitmarker = MakeClick("Moon1265_Hitmarker", 0f, 0.06f, 4000f)); } }

        private static AudioClip MakeGunshot()
        {
            int n = (int)(Rate * 0.35f);
            var data = new float[n];
            var rng = new System.Random(1265);
            float lowpass = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lowpass += (noise - lowpass) * 0.35f;
                float crack = noise * Mathf.Exp(-t * 60f);
                float body = lowpass * Mathf.Exp(-t * 14f);
                float thump = Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Exp(-t * 20f);
                data[i] = Mathf.Clamp(crack * 0.6f + body * 0.9f + thump * 0.7f, -1f, 1f);
            }
            return Create("Moon1265_Gunshot", data);
        }

        private static AudioClip MakeReload()
        {
            // Magazine out, magazine in, charging handle.
            int n = (int)(Rate * 1.6f);
            var data = new float[n];
            AddClick(data, 0.10f, 0.05f, 1800f, 0.6f);
            AddClick(data, 0.90f, 0.06f, 1400f, 0.9f);
            AddClick(data, 1.35f, 0.04f, 2600f, 0.7f);
            AddClick(data, 1.45f, 0.04f, 2200f, 0.7f);
            return Create("Moon1265_Reload", data);
        }

        private static AudioClip MakeClick(string name, float at, float length, float pitch)
        {
            var data = new float[(int)(Rate * (at + length))];
            AddClick(data, at, length, pitch, 0.8f);
            return Create(name, data);
        }

        private static void AddClick(float[] data, float at, float length, float pitch, float volume)
        {
            var rng = new System.Random((int)(pitch + at * 1000));
            int start = (int)(at * Rate);
            int count = (int)(length * Rate);
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float tone = Mathf.Sin(2 * Mathf.PI * pitch * t);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                data[start + i] += (tone * 0.6f + noise * 0.4f) * Mathf.Exp(-t * 90f) * volume;
            }
        }

        private static AudioClip Create(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
