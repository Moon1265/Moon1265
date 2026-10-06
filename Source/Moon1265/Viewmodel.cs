using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// The placeholder rifle drawn in front of the camera, plus its muzzle flash and tracer.
    /// Built from primitives; swap for a real model once the art exists.
    /// </summary>
    internal class Viewmodel
    {
        private static readonly Vector3 HipPosition = new Vector3(0.2f, -0.18f, 0.38f);
        // The rear sight sits 0.06 above the gun's origin, so this lines it up with the screen centre.
        private static readonly Vector3 AimPosition = new Vector3(0f, -0.06f, 0.3f);

        private readonly GameObject root;
        private readonly Transform muzzle;
        private readonly GameObject flash;
        private readonly LineRenderer tracer;

        private float kick;          // 0..1, decays after each shot
        private float flashUntil;
        private float tracerUntil;
        private float reloadDip;     // 0..1, lowers the gun while reloading

        public Viewmodel(Transform camera)
        {
            root = new GameObject("Moon1265_Viewmodel");
            root.layer = 0;
            root.transform.SetParent(camera, false);
            root.transform.localPosition = HipPosition;
            Transform t = root.transform;

            Color metal = new Color(0.16f, 0.16f, 0.17f);
            Color polymer = new Color(0.27f, 0.25f, 0.21f);

            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, 0f, 0f), new Vector3(0.06f, 0.09f, 0.42f), metal);            // receiver
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, -0.02f, -0.29f), new Vector3(0.05f, 0.08f, 0.18f), polymer);  // stock
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, -0.11f, 0.08f), Quaternion.Euler(12f, 0f, 0f), new Vector3(0.04f, 0.14f, 0.07f), metal); // magazine
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, -0.09f, -0.1f), Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.04f, 0.1f, 0.05f), polymer); // grip
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, 0.005f, 0.24f), new Vector3(0.055f, 0.07f, 0.12f), polymer);  // handguard
            Shapes.Visual(PrimitiveType.Cylinder, t, new Vector3(0f, 0.015f, 0.38f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.022f, 0.09f, 0.022f), metal); // barrel
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, 0.058f, -0.08f), new Vector3(0.03f, 0.025f, 0.03f), metal);   // rear sight
            Shapes.Visual(PrimitiveType.Cube, t, new Vector3(0f, 0.058f, 0.2f), new Vector3(0.008f, 0.025f, 0.008f), metal);   // front post

            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(t, false);
            muzzle.localPosition = new Vector3(0f, 0.015f, 0.48f);

            flash = Shapes.Visual(PrimitiveType.Sphere, muzzle, Vector3.zero, new Vector3(0.07f, 0.07f, 0.12f), new Color(1f, 0.85f, 0.4f));
            flash.SetActive(false);

            var tracerObject = new GameObject("Moon1265_Tracer");
            tracer = tracerObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.startWidth = 0.015f;
            tracer.endWidth = 0.008f;
            tracer.sharedMaterial = Shapes.Material(new Color(1f, 0.9f, 0.5f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            tracer.enabled = false;
        }

        public Vector3 MuzzlePosition { get { return muzzle.position; } }

        public void Fire(Vector3 hitPoint)
        {
            kick = 1f;
            flashUntil = Time.time + 0.04f;
            tracerUntil = Time.time + 0.035f;
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.SetActive(true);
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, hitPoint);
            tracer.enabled = true;
        }

        /// <param name="aim">0 = hip fire, 1 = fully aimed down sights.</param>
        public void Animate(float aim, bool reloading, float dt)
        {
            kick = Mathf.MoveTowards(kick, 0f, dt * 12f);
            reloadDip = Mathf.MoveTowards(reloadDip, reloading ? 1f : 0f, dt * 5f);

            Vector3 position = Vector3.Lerp(HipPosition, AimPosition, aim);
            position += new Vector3(0f, -0.12f * reloadDip, -0.06f * kick);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(-6f * kick + 25f * reloadDip, 0f, -20f * reloadDip);

            if (Time.time > flashUntil) flash.SetActive(false);
            if (Time.time > tracerUntil) tracer.enabled = false;
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            if (tracer != null) Object.Destroy(tracer.gameObject);
        }
    }
}
