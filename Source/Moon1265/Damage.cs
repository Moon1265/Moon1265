using System.Collections.Generic;
using UnityEngine;

namespace Moon1265
{
    /// <summary>Tracks bullet damage on parts and Kerbals, and blows them up when it runs out.</summary>
    internal static class Damage
    {
        private static readonly Dictionary<Part, float> taken = new Dictionary<Part, float>();

        /// <returns>True if the hit destroyed the part.</returns>
        public static bool Apply(Part part, float amount, Vector3 point, Vector3 direction)
        {
            // Vessels "on rails" (packed, e.g. landed ships more than ~350 m away) can't be pushed or
            // safely exploded; BDArmory skips them too.
            if (part.vessel == null || !part.vessel.loaded || part.vessel.packed) return false;

            Rigidbody rb = part.Rigidbody;
            if (rb != null) rb.AddForceAtPosition(direction * Settings.BulletImpulse, point, ForceMode.Impulse);

            float total;
            taken.TryGetValue(part, out total);
            total += amount;

            if (total < MaxHealth(part))
            {
                taken[part] = total;
                return false;
            }

            taken.Remove(part);
            part.explode();
            return true;
        }

        /// <summary>True for Kerbals, including ones sitting in external command seats.</summary>
        public static bool IsKerbal(Part part)
        {
            return part.FindModuleImplementing<KerbalEVA>() != null;
        }

        public static float MaxHealth(Part part)
        {
            if (IsKerbal(part)) return Settings.KerbalHealth;
            return Settings.PartHealthBase + Settings.PartHealthPerTonne * part.mass;
        }

        public static void Clear()
        {
            taken.Clear();
        }
    }
}
