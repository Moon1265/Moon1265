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
        // Controls. MW2 mode is toggled with Ctrl+Shift+Y by default; while it is on, every KSP
        // key binding is locked out and these keys belong to MW2 mode.
        public static KeyCode ToggleKey = KeyCode.Y;
        public static bool ToggleNeedsCtrl = true;
        public static bool ToggleNeedsShift = true;
        public static KeyCode ForwardKey = KeyCode.W;
        public static KeyCode BackKey = KeyCode.S;
        public static KeyCode LeftKey = KeyCode.A;
        public static KeyCode RightKey = KeyCode.D;
        public static KeyCode SprintKey = KeyCode.LeftShift;
        public static KeyCode JumpKey = KeyCode.Space;
        public static KeyCode CrouchKey = KeyCode.C;
        public static KeyCode CrouchKeyAlt = KeyCode.LeftControl;
        public static KeyCode ReloadKey = KeyCode.R;
        public static float MouseSensitivity = 2f;
        public static bool InvertMouse = false;

        // Movement (metres, seconds). Replaces KSP's EVA walking while MW2 mode is on.
        public static float WalkSpeed = 3.5f;
        public static float SprintSpeed = 6f;
        public static float CrouchSpeed = 1.75f;
        public static float AimSpeedMultiplier = 0.6f;
        public static float GroundAcceleration = 40f;
        public static float AirAcceleration = 6f;
        public static float JumpSpeed = 3.5f;
        public static float CrouchEyeDrop = 0.3f;
        // Tactical sprint: double-tap sprint and keep holding it. Faster, rifle pointed up.
        public static float TacticalSprintSpeed = 7.5f;
        public static float TacticalSprintDuration = 4f;   // seconds before it drops to a normal sprint (0 = unlimited)
        public static float TacticalSprintRecharge = 3f;   // seconds to fully recharge
        public static float DoubleTapTime = 0.3f;
        // Slide: press crouch while sprinting.
        public static float SlideSpeed = 8.5f;             // starting speed (or your sprint speed if faster)
        public static float SlideFriction = 7f;            // m/s lost per second (slopes add or remove speed)
        public static float SlideEndSpeed = 2.5f;
        public static float SlideMaxTime = 1.4f;
        public static float SlideSteering = 30f;           // degrees per second
        public static float SlideCooldown = 0.5f;
        public static float SlideEyeDrop = 0.12f;          // extra on top of the crouch drop
        public static float SlideCameraTilt = 6f;          // degrees of roll
        public static float SprintFovBoost = 5f;           // extra field of view while tactical sprinting or sliding
        // Gravity used in MW2 mode when the real gravity is weaker (0 = always use real gravity).
        // Keeps ground combat on low-gravity moons from turning into bunny hopping.
        public static float CombatGravity = 9.81f;
        // KSP ragdolls a Kerbal that lands faster than 3.5 m/s; raised while MW2 mode is on.
        public static float StumbleThreshold = 15f;

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

            ToggleKey = Key(n, "toggleKey", ToggleKey);
            ToggleNeedsCtrl = Bool(n, "toggleNeedsCtrl", ToggleNeedsCtrl);
            ToggleNeedsShift = Bool(n, "toggleNeedsShift", ToggleNeedsShift);
            ForwardKey = Key(n, "forwardKey", ForwardKey);
            BackKey = Key(n, "backKey", BackKey);
            LeftKey = Key(n, "leftKey", LeftKey);
            RightKey = Key(n, "rightKey", RightKey);
            SprintKey = Key(n, "sprintKey", SprintKey);
            JumpKey = Key(n, "jumpKey", JumpKey);
            CrouchKey = Key(n, "crouchKey", CrouchKey);
            CrouchKeyAlt = Key(n, "crouchKeyAlt", CrouchKeyAlt);
            ReloadKey = Key(n, "reloadKey", ReloadKey);
            MouseSensitivity = Float(n, "mouseSensitivity", MouseSensitivity);
            InvertMouse = Bool(n, "invertMouse", InvertMouse);

            WalkSpeed = Float(n, "walkSpeed", WalkSpeed);
            SprintSpeed = Float(n, "sprintSpeed", SprintSpeed);
            CrouchSpeed = Float(n, "crouchSpeed", CrouchSpeed);
            AimSpeedMultiplier = Float(n, "aimSpeedMultiplier", AimSpeedMultiplier);
            GroundAcceleration = Float(n, "groundAcceleration", GroundAcceleration);
            AirAcceleration = Float(n, "airAcceleration", AirAcceleration);
            JumpSpeed = Float(n, "jumpSpeed", JumpSpeed);
            CrouchEyeDrop = Float(n, "crouchEyeDrop", CrouchEyeDrop);
            TacticalSprintSpeed = Float(n, "tacticalSprintSpeed", TacticalSprintSpeed);
            TacticalSprintDuration = Float(n, "tacticalSprintDuration", TacticalSprintDuration);
            TacticalSprintRecharge = Float(n, "tacticalSprintRecharge", TacticalSprintRecharge);
            DoubleTapTime = Float(n, "doubleTapTime", DoubleTapTime);
            SlideSpeed = Float(n, "slideSpeed", SlideSpeed);
            SlideFriction = Float(n, "slideFriction", SlideFriction);
            SlideEndSpeed = Float(n, "slideEndSpeed", SlideEndSpeed);
            SlideMaxTime = Float(n, "slideMaxTime", SlideMaxTime);
            SlideSteering = Float(n, "slideSteering", SlideSteering);
            SlideCooldown = Float(n, "slideCooldown", SlideCooldown);
            SlideEyeDrop = Float(n, "slideEyeDrop", SlideEyeDrop);
            SlideCameraTilt = Float(n, "slideCameraTilt", SlideCameraTilt);
            SprintFovBoost = Float(n, "sprintFovBoost", SprintFovBoost);
            CombatGravity = Float(n, "combatGravity", CombatGravity);
            StumbleThreshold = Float(n, "stumbleThreshold", StumbleThreshold);

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
