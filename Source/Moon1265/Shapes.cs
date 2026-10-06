using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moon1265
{
    /// <summary>Builds placeholder geometry out of Unity primitives until we have real models.</summary>
    internal static class Shapes
    {
        private static Shader shader;
        // One material per colour, shared by everything, so bullet impacts don't leak materials.
        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        public static Material Material(Color color)
        {
            Material cached;
            if (materials.TryGetValue(color, out cached) && cached != null) return cached;

            if (shader == null)
            {
                string[] candidates = { "KSP/Unlit", "KSP/Emissive/Diffuse", "KSP/Diffuse", "Unlit/Color", "Standard" };
                foreach (string name in candidates)
                {
                    shader = Shader.Find(name);
                    if (shader != null) break;
                }
            }
            cached = new Material(shader) { color = color };
            materials[color] = cached;
            return cached;
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

        // ---------------------------------------------------------------- shaded viewmodel parts

        // A fixed "key light" in viewmodel space (above, left and slightly behind the eye).
        private static readonly Vector3 KeyLight = new Vector3(-0.4f, 0.8f, -0.45f).normalized;
        private static Texture2D shadeRamp;
        private static readonly Dictionary<Color, Material> shadedMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<string, Mesh> shadedMeshes = new Dictionary<string, Mesh>();

        /// <summary>
        /// Like Visual, but with simple lighting baked into the mesh: each face picks a brightness
        /// from a small grey ramp according to how much it faces a fixed key light. Works with the
        /// unlit shader, so the gun reads clearly whatever the in-game lighting is.
        /// </summary>
        public static GameObject ShadedVisual(PrimitiveType type, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Color color)
        {
            GameObject go = Visual(type, parent, localPosition, localRotation, localScale, color);
            MeshFilter filter = go.GetComponent<MeshFilter>();
            filter.sharedMesh = ShadedMesh(type, filter.sharedMesh, localRotation);
            go.GetComponent<Renderer>().sharedMaterial = ShadedMaterial(color);
            return go;
        }

        private static Mesh ShadedMesh(PrimitiveType type, Mesh source, Quaternion rotation)
        {
            string key = type + "/" + rotation.eulerAngles;
            Mesh mesh;
            if (shadedMeshes.TryGetValue(key, out mesh) && mesh != null) return mesh;

            mesh = Object.Instantiate(source);
            Vector3[] normals = mesh.normals;
            var uv = new Vector2[normals.Length];
            for (int i = 0; i < normals.Length; i++)
                uv[i] = new Vector2(Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(rotation * normals[i], KeyLight)), 0.5f);
            mesh.uv = uv;
            shadedMeshes[key] = mesh;
            return mesh;
        }

        private static Material ShadedMaterial(Color color)
        {
            Material material;
            if (shadedMaterials.TryGetValue(color, out material) && material != null) return material;

            material = new Material(Material(color));
            if (material.HasProperty("_MainTex")) material.mainTexture = ShadeRamp();
            shadedMaterials[color] = material;
            return material;
        }

        private static Texture2D ShadeRamp()
        {
            if (shadeRamp != null) return shadeRamp;
            const int steps = 8;
            shadeRamp = new Texture2D(steps, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int i = 0; i < steps; i++)
            {
                float v = Mathf.Lerp(0.45f, 1f, i / (float)(steps - 1));
                shadeRamp.SetPixel(i, 0, new Color(v, v, v, 1f));
            }
            shadeRamp.Apply(false, true);
            return shadeRamp;
        }

        // ---------------------------------------------------------------- glowing effects

        private static Material glow;

        /// <summary>
        /// Additive "glow" material for the muzzle flash and tracers (KSP's particle shader), tinted by
        /// vertex colour. Falls back to the flat material if the shader isn't there.
        /// </summary>
        public static Material Glow()
        {
            if (glow != null) return glow;
            Shader additive = Shader.Find("KSP/Particles/Additive");
            if (additive == null) additive = Shader.Find("Legacy Shaders/Particles/Additive");
            if (additive == null) return glow = Material(new Color(1f, 0.85f, 0.4f));

            glow = new Material(additive);
            if (glow.HasProperty("_TintColor")) glow.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return glow;
        }

        /// <summary>A copy of a primitive's mesh with every vertex given one colour (for the glow shader).</summary>
        public static GameObject GlowVisual(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject go = Visual(type, parent, localPosition, Quaternion.identity, localScale, color);
            MeshFilter filter = go.GetComponent<MeshFilter>();
            string key = "glow/" + type + "/" + color;
            Mesh mesh;
            if (!shadedMeshes.TryGetValue(key, out mesh) || mesh == null)
            {
                mesh = Object.Instantiate(filter.sharedMesh);
                var colors = new Color[mesh.vertexCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = color;
                mesh.colors = colors;
                shadedMeshes[key] = mesh;   // cached, so entering MW2 mode again doesn't leak meshes
            }
            filter.sharedMesh = mesh;
            go.GetComponent<Renderer>().sharedMaterial = Glow();
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
