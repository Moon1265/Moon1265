using System;
using System.Globalization;
using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// Tunables, read from the MOON1265_SETTINGS node in GameData/Moon1265/Settings.cfg.
    /// Anything missing from the file keeps the default below.
    /// </summary>
    internal static class Settings
    {
        // Controls
        public static KeyCode FirstPersonKey = KeyCode.Y;
        public static KeyCode ReloadKey = KeyCode.T;
        public static float MouseSensitivity = 2f;
        public static bool InvertMouse = false;

        // Camera
        public static float FieldOfView = 70f;
        public static float AimFieldOfView = 45f;
        public static float EyeHeight = 0.35f;

        // Weapon
        public static int MagazineSize = 30;
        public static float RoundsPerMinute = 600f;
        public static float ReloadTime = 2.2f;
        public static float Range = 1000f;
        public static float HipSpread = 1.5f;   // degrees
        public static float AimSpread = 0.15f;  // degrees
        public static float Recoil = 1.0f;      // degrees of upward kick per shot
        public static float SoundVolume = 0.7f;

        // Damage. KSP measures mass in tonnes and force in kN, so impulses are in kN·s.
        public static float Damage = 25f;
        public static float KerbalHealth = 100f;
        public static float PartHealthBase = 40f;
        public static float PartHealthPerTonne = 400f;
        public static float BulletImpulse = 0.02f;   // push on whatever is hit
        public static float RecoilImpulse = 0.004f;  // push back on the shooter (noticeable in space)

        private static bool loaded;

        public static void Load()
        {
            if (loaded || GameDatabase.Instance == null) return;
            loaded = true;

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("MOON1265_SETTINGS");
            if (nodes == null || nodes.Length == 0) return;
            ConfigNode n = nodes[0];

            FirstPersonKey = Key(n, "firstPersonKey", FirstPersonKey);
            ReloadKey = Key(n, "reloadKey", ReloadKey);
            MouseSensitivity = Float(n, "mouseSensitivity", MouseSensitivity);
            InvertMouse = Bool(n, "invertMouse", InvertMouse);

            FieldOfView = Float(n, "fieldOfView", FieldOfView);
            AimFieldOfView = Float(n, "aimFieldOfView", AimFieldOfView);
            EyeHeight = Float(n, "eyeHeight", EyeHeight);

            MagazineSize = Mathf.Max(1, (int)Float(n, "magazineSize", MagazineSize));
            RoundsPerMinute = Mathf.Max(1f, Float(n, "roundsPerMinute", RoundsPerMinute));
            ReloadTime = Float(n, "reloadTime", ReloadTime);
            Range = Float(n, "range", Range);
            HipSpread = Float(n, "hipSpread", HipSpread);
            AimSpread = Float(n, "aimSpread", AimSpread);
            Recoil = Float(n, "recoil", Recoil);
            SoundVolume = Float(n, "soundVolume", SoundVolume);

            Damage = Float(n, "damage", Damage);
            KerbalHealth = Float(n, "kerbalHealth", KerbalHealth);
            PartHealthBase = Float(n, "partHealthBase", PartHealthBase);
            PartHealthPerTonne = Float(n, "partHealthPerTonne", PartHealthPerTonne);
            BulletImpulse = Float(n, "bulletImpulse", BulletImpulse);
            RecoilImpulse = Float(n, "recoilImpulse", RecoilImpulse);
        }

        private static KeyCode Key(ConfigNode n, string name, KeyCode fallback)
        {
            string v = n.GetValue(name);
            if (string.IsNullOrEmpty(v)) return fallback;
            try
            {
                return (KeyCode)Enum.Parse(typeof(KeyCode), v.Trim(), true);
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[Moon1265] Unknown key '" + v + "' for " + name + "; using " + fallback);
                return fallback;
            }
        }

        private static float Float(ConfigNode n, string name, float fallback)
        {
            string v = n.GetValue(name);
            float result;
            return v != null && float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static bool Bool(ConfigNode n, string name, bool fallback)
        {
            string v = n.GetValue(name);
            bool result;
            return v != null && bool.TryParse(v.Trim(), out result) ? result : fallback;
        }
    }
}
