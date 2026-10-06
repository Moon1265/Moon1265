using UnityEngine;
using UnityEngine.Rendering;

namespace Moon1265
{
    /// <summary>Builds placeholder geometry out of Unity primitives until we have real models.</summary>
    internal static class Shapes
    {
        private static Shader shader;

        public static Material Material(Color color)
        {
            if (shader == null)
            {
                string[] candidates = { "KSP/Unlit", "KSP/Emissive/Diffuse", "KSP/Diffuse", "Unlit/Color", "Standard" };
                foreach (string name in candidates)
                {
                    shader = Shader.Find(name);
                    if (shader != null) break;
                }
            }
            return new Material(shader) { color = color };
        }

        /// <summary>Creates a primitive with no collider (purely visual).</summary>
        public static GameObject Visual(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
        {
            return Visual(type, parent, localPosition, Quaternion.identity, localScale, color);
        }

        public static GameObject Visual(PrimitiveType type, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.layer = parent != null ? parent.gameObject.layer : 0;

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Material(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>A short-lived puff where a bullet lands.</summary>
        public static void Impact(Vector3 point, Vector3 normal)
        {
            GameObject go = Visual(PrimitiveType.Sphere, null, point + normal * 0.03f, Vector3.one * 0.12f, new Color(0.35f, 0.33f, 0.3f));
            go.name = "Moon1265_Impact";
            Object.Destroy(go, 0.4f);
        }
    }
}
