using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// The placeholder rifle drawn in front of the camera, plus its muzzle flash and tracer.
    /// The rifle lives on its own layer and is drawn by a dedicated camera on top of the world
    /// (the usual shooter "viewmodel" setup), so KSP's near clip plane, walls and the ground can
    /// never cut into it. Built from primitives; swap for a real model once the art exists.
    /// </summary>
    internal class Viewmodel
    {
        // Layer 22 ("KerbalInstructors") is only used for the instructor portraits, which live far away
        // in their own space. Our camera only sees 3 m, so it never picks those up.
        private const int ViewmodelLayer = 22;

        private static readonly Vector3 HipPosition = new Vector3(0.2f, -0.18f, 0.38f);
        // Aimed down sights the front post's tip sits just under the screen centre (a "6 o'clock hold"),
        // so the sights never cover what you're shooting at.
        private static readonly Vector3 AimPosition = new Vector3(0f, -0.073f, 0.3f);
        private static readonly Vector3 SprintPosition = new Vector3(0.12f, -0.24f, 0.32f);
        private static readonly Quaternion SprintRotation = Quaternion.Euler(10f, -35f, 15f);

        private readonly Camera mainCamera;
        private readonly int savedMainCullingMask;
        private readonly Camera camera;
        private readonly GameObject root;
        private readonly Transform muzzle;
        private readonly GameObject flash;
        private readonly LineRenderer tracer;

        private float kick;          // 0..1, decays after each shot
        private float flashUntil;
        private float tracerUntil;
        private float reloadDip;     // 0..1, lowers the gun while reloading
        private float sprint;        // 0..1, sprint pose
        private float bobPhase;
        private float bobAmount;

        public Viewmodel(Camera mainCamera)
        {
            this.mainCamera = mainCamera;
            // Only our camera may draw the rifle; a copy drawn by the world camera would be clipped.
            savedMainCullingMask = mainCamera.cullingMask;
            mainCamera.cullingMask &= ~(1 << ViewmodelLayer);

            var cameraObject = new GameObject("Moon1265_ViewmodelCamera");
            cameraObject.transform.SetParent(mainCamera.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;   // draw over the world, keep its colours
            camera.cullingMask = 1 << ViewmodelLayer;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 3f;
            camera.depth = mainCamera.depth + 0.5f;        // right after the world, before KSP's UI
            camera.fieldOfView = mainCamera.fieldOfView;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            root = new GameObject("Moon1265_Viewmodel");
            root.layer = ViewmodelLayer;
            root.transform.SetParent(cameraObject.transform, false);
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
            muzzle.gameObject.layer = ViewmodelLayer;
            muzzle.SetParent(t, false);
            muzzle.localPosition = new Vector3(0f, 0.015f, 0.48f);

            flash = Shapes.Visual(PrimitiveType.Sphere, muzzle, Vector3.zero, new Vector3(0.07f, 0.07f, 0.12f), new Color(1f, 0.85f, 0.4f));
            flash.SetActive(false);

            // The tracer flies out into the world, so it is drawn by the normal camera.
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

        public void Fire(Vector3 hitPoint)
        {
            kick = 1f;
            flashUntil = Time.time + 0.04f;
            tracerUntil = Time.time + 0.035f;
            flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flash.SetActive(true);

            // The muzzle is drawn by the viewmodel camera; find the matching point in the world
            // so the tracer appears to leave the barrel.
            Vector3 screen = camera.WorldToViewportPoint(muzzle.position);
            Vector3 start = mainCamera.ViewportToWorldPoint(new Vector3(screen.x, screen.y, 0.6f));
            tracer.SetPosition(0, start);
            tracer.SetPosition(1, hitPoint);
            tracer.enabled = true;
        }

        /// <param name="aim">0 = hip fire, 1 = fully aimed down sights.</param>
        /// <param name="speed">Horizontal ground speed in m/s, for the walking bob.</param>
        public void Animate(float aim, bool reloading, bool sprinting, float speed, float dt)
        {
            camera.fieldOfView = mainCamera.fieldOfView;

            kick = Mathf.MoveTowards(kick, 0f, dt * 12f);
            reloadDip = Mathf.MoveTowards(reloadDip, reloading ? 1f : 0f, dt * 5f);
            sprint = Mathf.MoveTowards(sprint, sprinting ? 1f : 0f, dt * 6f);

            // Walking bob: a figure-eight sway that scales with speed and almost vanishes when aiming.
            bobPhase += dt * Mathf.Lerp(6f, 13f, Mathf.Clamp01(speed / 6f)) * (speed > 0.2f ? 1f : 0f);
            bobAmount = Mathf.MoveTowards(bobAmount, Mathf.Clamp01(speed / 4f), dt * 4f);
            float bobScale = bobAmount * Mathf.Lerp(1f, 0.15f, aim);
            Vector3 bob = new Vector3(Mathf.Sin(bobPhase) * 0.012f, -Mathf.Abs(Mathf.Cos(bobPhase)) * 0.01f, 0f) * bobScale;

            Vector3 position = Vector3.Lerp(Vector3.Lerp(HipPosition, AimPosition, aim), SprintPosition, sprint);
            position += bob + new Vector3(0f, -0.12f * reloadDip, -0.06f * kick);
            root.transform.localPosition = position;
            root.transform.localRotation =
                Quaternion.Slerp(Quaternion.identity, SprintRotation, sprint) *
                Quaternion.Euler(-6f * kick + 25f * reloadDip, 0f, -20f * reloadDip);

            if (Time.time > flashUntil) flash.SetActive(false);
            if (Time.time > tracerUntil) tracer.enabled = false;
        }

        public void Destroy()
        {
            if (mainCamera != null && (savedMainCullingMask & (1 << ViewmodelLayer)) != 0)
                mainCamera.cullingMask |= 1 << ViewmodelLayer;
            if (camera != null) Object.Destroy(camera.gameObject);
            if (tracer != null) Object.Destroy(tracer.gameObject);
        }
    }
}
