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

        public static float MaxHealth(Part part)
        {
            if (part.vessel != null && part.vessel.isEVA) return Settings.KerbalHealth;
            return Settings.PartHealthBase + Settings.PartHealthPerTonne * part.mass;
        }

        public static void Clear()
        {
            taken.Clear();
        }
    }
}
