using System.Collections.Generic;
using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// First-person mode for EVA Kerbals: takes over the flight camera, puts it behind the
    /// Kerbal's eyes with mouse look, and gives them a rifle that damages Kerbals and ship parts.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FirstPersonController : MonoBehaviour
    {
        // Layers a bullet can hit: 0 Default (parts), 15 Local Scenery (terrain, buildings),
        // 17 EVA, 19 PhysicalObjects, 28 TerrainColliders.
        private const int HitMask = (1 << 0) | (1 << 15) | (1 << 17) | (1 << 19) | (1 << 28);
        private const float HintDuration = 8f;
        private const float AimTime = 0.15f;

        private bool active;
        private KerbalEVA kerbal;

        private FlightCamera flightCamera;
        private Transform cameraRig;
        private Camera mainCamera;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private float savedFov;
        private float savedNearClip;

        private Vector3 lookForward;
        private float pitch;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

        private Viewmodel viewmodel;
        private AudioSource audioSource;

        private int ammo;
        private bool reloading;
        private float reloadDoneAt;
        private float nextShotAt;
        private float aim;    // 0 = hip, 1 = aimed down sights
        private float bloom;  // extra spread from sustained fire, in degrees
        private float hitmarkerUntil;
        private bool hitmarkerKill;
        private float hintUntil;
        private int kerbalKills;
        private int partsDestroyed;

        private GUIStyle bigText;
        private GUIStyle smallText;
        private GUIStyle hintText;

        private void Awake()
        {
            Settings.Load();
            ammo = Settings.MagazineSize;
        }

        private void Start()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            Exit();
            Damage.Clear();
        }

        private void OnVesselChange(Vessel vessel)
        {
            Exit();
        }

        private void Update()
        {
            if (!active)
            {
                if (Input.GetKeyDown(Settings.FirstPersonKey) && CanEnter()) Enter();
                return;
            }

            if (!StillValid() || Input.GetKeyDown(Settings.FirstPersonKey))
            {
                Exit();
                return;
            }

            if (FlightDriver.Pause)
            {
                SetCursorLocked(false);
                return;
            }

            SetCursorLocked(true);
            UpdateLook();
            UpdateWeapon();
        }

        private void LateUpdate()
        {
            if (!active || !StillValid()) return;
            PlaceCamera();
            viewmodel.Animate(aim, reloading, Time.deltaTime);
        }

        // ---------------------------------------------------------------- entering / leaving

        private bool CanEnter()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.isEVA || MapView.MapIsEnabled || FlightCamera.fetch == null) return false;
            if (CameraManager.Instance != null && CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) return false;

            kerbal = vessel.FindPartModuleImplementing<KerbalEVA>();
            return kerbal != null;
        }

        private bool StillValid()
        {
            if (kerbal == null || kerbal.vessel == null || kerbal.vessel != FlightGlobals.ActiveVessel) return false;
            if (MapView.MapIsEnabled) return false;
            return CameraManager.Instance == null || CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Flight;
        }

        private void Enter()
        {
            flightCamera = FlightCamera.fetch;
            cameraRig = flightCamera.transform;
            mainCamera = flightCamera.mainCamera;

            savedLocalPosition = cameraRig.localPosition;
            savedLocalRotation = cameraRig.localRotation;
            savedFov = mainCamera.fieldOfView;
            savedNearClip = mainCamera.nearClipPlane;

            // Stop KSP's orbit camera from moving the camera; we place it ourselves every frame.
            flightCamera.DeactivateUpdate();
            mainCamera.nearClipPlane = 0.05f;
            flightCamera.SetFoV(Settings.FieldOfView);

            Vector3 up = Up();
            lookForward = Vector3.ProjectOnPlane(mainCamera.transform.forward, up);
            if (lookForward.sqrMagnitude < 1e-4f) lookForward = Vector3.ProjectOnPlane(kerbal.transform.forward, up);
            lookForward.Normalize();
            pitch = 0f;

            HideKerbal();
            viewmodel = new Viewmodel(mainCamera.transform);
            audioSource = mainCamera.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            aim = 0f;
            bloom = 0f;
            reloading = false;
            hintUntil = Time.time + HintDuration;
            active = true;

            PlaceCamera();
        }

        private void Exit()
        {
            if (!active) return;
            active = false;

            ShowKerbal();
            if (viewmodel != null) viewmodel.Destroy();
            viewmodel = null;
            if (audioSource != null) Destroy(audioSource);
            audioSource = null;

            if (cameraRig != null)
            {
                cameraRig.localPosition = savedLocalPosition;
                cameraRig.localRotation = savedLocalRotation;
            }
            if (mainCamera != null) mainCamera.nearClipPlane = savedNearClip;
            if (flightCamera != null)
            {
                flightCamera.SetFoV(savedFov);
                flightCamera.ActivateUpdate();
            }

            SetCursorLocked(false);
            reloading = false;
            kerbal = null;
        }

        private void HideKerbal()
        {
            hiddenRenderers.Clear();
            foreach (Renderer r in kerbal.part.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
        }

        private void ShowKerbal()
        {
            foreach (Renderer r in hiddenRenderers)
                if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ---------------------------------------------------------------- camera

        /// <summary>"Up" for the look controls: away from the centre of the body we're near.</summary>
        private Vector3 Up()
        {
            Vessel vessel = kerbal.vessel;
            return ((Vector3)(vessel.CoMD - vessel.mainBody.position)).normalized;
        }

        private void UpdateLook()
        {
            float sensitivity = Settings.MouseSensitivity * Mathf.Lerp(1f, Settings.AimFieldOfView / Settings.FieldOfView, aim);
            float yaw = Input.GetAxis("Mouse X") * sensitivity;
            float rise = Input.GetAxis("Mouse Y") * sensitivity * (Settings.InvertMouse ? -1f : 1f);

            Vector3 up = Up();
            lookForward = Vector3.ProjectOnPlane(lookForward, up);
            if (lookForward.sqrMagnitude < 1e-6f) lookForward = Vector3.ProjectOnPlane(kerbal.transform.forward, up);
            lookForward = Quaternion.AngleAxis(yaw, up) * lookForward.normalized;
            pitch = Mathf.Clamp(pitch - rise, -89f, 89f);
        }

        private void PlaceCamera()
        {
            Vector3 up = Up();
            Quaternion look = Quaternion.LookRotation(lookForward, up) * Quaternion.Euler(pitch, 0f, 0f);

            Transform body = kerbal.transform;
            Vector3 eye = body.position + body.up * Settings.EyeHeight;

            // The FlightCamera component may sit above the rendering camera in the hierarchy,
            // so move the rig such that the camera itself ends up at the eye, looking along `look`.
            Quaternion cameraInRig = Quaternion.Inverse(cameraRig.rotation) * mainCamera.transform.rotation;
            cameraRig.rotation = look * Quaternion.Inverse(cameraInRig);
            cameraRig.position += eye - mainCamera.transform.position;
        }

        // ---------------------------------------------------------------- weapon

        private void UpdateWeapon()
        {
            float dt = Time.deltaTime;
            bool aiming = Input.GetMouseButton(1) && !reloading;
            aim = Mathf.MoveTowards(aim, aiming ? 1f : 0f, dt / AimTime);
            flightCamera.SetFoV(Mathf.Lerp(Settings.FieldOfView, Settings.AimFieldOfView, aim));
            bloom = Mathf.MoveTowards(bloom, 0f, dt * 4f);

            if (reloading)
            {
                if (Time.time < reloadDoneAt) return;
                reloading = false;
                ammo = Settings.MagazineSize;
            }

            if (Input.GetKeyDown(Settings.ReloadKey) && ammo < Settings.MagazineSize)
            {
                StartReload();
                return;
            }

            if (Input.GetMouseButton(0) && Time.time >= nextShotAt)
            {
                if (ammo > 0)
                {
                    Fire();
                }
                else
                {
                    Play(Sounds.DryFire, 1f);
                    StartReload();
                }
            }
        }

        private void StartReload()
        {
            reloading = true;
            reloadDoneAt = Time.time + Settings.ReloadTime;
            Play(Sounds.Reload, 1f);
        }

        private void Fire()
        {
            ammo--;
            nextShotAt = Time.time + 60f / Settings.RoundsPerMinute;

            Transform cam = mainCamera.transform;
            float spread = Mathf.Lerp(Settings.HipSpread, Settings.AimSpread, aim) + bloom;
            Vector2 offset = Random.insideUnitCircle * spread;
            Vector3 direction = cam.rotation * (Quaternion.Euler(offset.y, offset.x, 0f) * Vector3.forward);

            RaycastHit hit;
            bool didHit = FindHit(cam.position, direction, out hit);
            viewmodel.Fire(didHit ? hit.point : cam.position + direction * Settings.Range);
            Play(Sounds.Gunshot, 1f);

            // Recoil: kick the view up, bloom the spread, and push the shooter back (you'll feel it in space).
            pitch = Mathf.Clamp(pitch - Settings.Recoil * Mathf.Lerp(1f, 0.5f, aim), -89f, 89f);
            lookForward = Quaternion.AngleAxis(Random.Range(-0.3f, 0.3f) * Settings.Recoil, Up()) * lookForward;
            bloom = Mathf.Min(bloom + 0.4f * (1f - 0.7f * aim), 3f);
            Rigidbody self = kerbal.part.Rigidbody;
            if (self != null) self.AddForce(-direction * Settings.RecoilImpulse, ForceMode.Impulse);

            if (!didHit) return;
            Shapes.Impact(hit.point, hit.normal);

            Part part = hit.collider.GetComponentInParent<Part>();
            if (part == null) return;

            bool isKerbal = part.vessel != null && part.vessel.isEVA;
            bool destroyed = Damage.Apply(part, Settings.Damage, hit.point, direction);
            if (destroyed)
            {
                if (isKerbal) kerbalKills++;
                else partsDestroyed++;
            }
            hitmarkerUntil = Time.time + 0.15f;
            hitmarkerKill = destroyed;
            Play(Sounds.Hitmarker, 0.6f);
        }

        /// <summary>Nearest thing along the ray that isn't the shooter.</summary>
        private bool FindHit(Vector3 origin, Vector3 direction, out RaycastHit result)
        {
            result = default(RaycastHit);
            bool found = false;
            float best = float.MaxValue;

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, Settings.Range, HitMask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.distance >= best) continue;
                Part part = h.collider.GetComponentInParent<Part>();
                if (part != null && part.vessel == kerbal.vessel) continue;
                best = h.distance;
                result = h;
                found = true;
            }
            return found;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (audioSource != null) audioSource.PlayOneShot(clip, Settings.SoundVolume * GameSettings.SHIP_VOLUME * volume);
        }

        // ---------------------------------------------------------------- HUD

        private void OnGUI()
        {
            if (!active || FlightDriver.Pause) return;
            EnsureStyles();

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;

            if (aim < 0.9f)
            {
                float spread = Mathf.Lerp(Settings.HipSpread, Settings.AimSpread, aim) + bloom;
                DrawCrosshair(cx, cy, 8f + spread * 8f, new Color(1f, 1f, 1f, 0.85f * (1f - aim)));
            }
            else
            {
                DrawRect(cx - 1.5f, cy - 1.5f, 3f, 3f, new Color(1f, 0.2f, 0.2f, 0.9f));
            }

            if (Time.time < hitmarkerUntil) DrawHitmarker(cx, cy, hitmarkerKill ? new Color(1f, 0.25f, 0.2f) : Color.white);

            float right = Screen.width - 40f;
            float bottom = Screen.height - 40f;
            ShadowLabel(new Rect(right - 300f, bottom - 90f, 300f, 50f), reloading ? "RELOADING" : ammo + " / " + Settings.MagazineSize, bigText);
            ShadowLabel(new Rect(right - 300f, bottom - 40f, 300f, 30f), "Kerbals: " + kerbalKills + "   Parts: " + partsDestroyed, smallText);

            if (Time.time < hintUntil)
            {
                string hint = "[" + Settings.FirstPersonKey + "] exit first person    [LMB] fire    [RMB] aim    [" + Settings.ReloadKey + "] reload";
                ShadowLabel(new Rect(0f, 60f, Screen.width, 30f), hint, hintText);
            }
        }

        private void EnsureStyles()
        {
            if (bigText != null) return;
            bigText = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            smallText = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperRight };
            hintText = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        }

        private static void ShadowLabel(Rect rect, string text, GUIStyle style)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, text, style);
        }

        private static void DrawCrosshair(float cx, float cy, float gap, Color color)
        {
            const float length = 10f, thickness = 2f;
            DrawRect(cx - thickness / 2f, cy - gap - length, thickness, length, color);
            DrawRect(cx - thickness / 2f, cy + gap, thickness, length, color);
            DrawRect(cx - gap - length, cy - thickness / 2f, length, thickness, color);
            DrawRect(cx + gap, cy - thickness / 2f, length, thickness, color);
        }

        private static void DrawHitmarker(float cx, float cy, Color color)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            DrawCrosshair(cx, cy, 6f, color);
            GUI.matrix = saved;
        }

        private static void DrawRect(float x, float y, float w, float h, Color color)
        {
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = saved;
        }
    }
}
