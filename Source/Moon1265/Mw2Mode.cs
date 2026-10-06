using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// MW2 mode for EVA Kerbals, toggled with Ctrl+Shift+Y. While it is on:
    ///  - every KSP key binding is locked out (Escape still pauses), so all keys belong to MW2 mode;
    ///  - the camera sits in the Kerbal's head with mouse look;
    ///  - KSP's EVA walking is replaced by shooter movement (see CombatMovement);
    ///  - the Kerbal carries a rifle that damages Kerbals and ship parts.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Mw2Mode : MonoBehaviour
    {
        private const string LockId = "Moon1265_MW2Mode";
        // Every KSP control except the pause menu.
        private const ControlTypes LockedControls = ControlTypes.All & ~ControlTypes.PAUSE;

        // Layers a bullet can hit: 0 Default (parts), 15 Local Scenery (terrain, buildings),
        // 17 EVA, 19 PhysicalObjects, 26 wheel and landing-gear colliders, 28 TerrainColliders.
        private const int HitMask = (1 << 0) | (1 << 15) | (1 << 17) | (1 << 19) | (1 << 26) | (1 << 28);
        private const float HintDuration = 10f;
        private const float AimTime = 0.15f;

        // FlightCamera only refreshes its reference frame in its own LateUpdate, which we switch off.
        // Parts of KerbalEVA still read it as "up", so we keep it current ourselves.
        private static readonly FieldInfo CameraFrameField =
            typeof(FlightCamera).GetField("tgtFoR", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool active;
        private KerbalEVA kerbal;
        private CombatMovement movement;

        private FlightCamera flightCamera;
        private Transform cameraRig;
        private Camera mainCamera;
        private Vector3 savedLocalPosition;
        private Quaternion savedLocalRotation;
        private float savedFov;

        private Vector3 lookForward;
        private float pitch;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

        // Input sampled in Update, consumed in FixedUpdate.
        private MoveInput moveInput;
        private bool tacticalLatched;    // sprint was double-tapped and is still held
        private float lastSprintTapAt = -10f;
        private float speedFov;          // 0..1, smoothed extra FOV while tactical sprinting or sliding

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

        private bool uiHidden;
        private bool cursorLockedByUs;

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
            GameEvents.OnCameraChange.Add(OnCameraChange);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
        }

        private void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.OnCameraChange.Remove(OnCameraChange);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            Exit();
            Damage.Clear();
        }

        private void OnVesselChange(Vessel vessel)
        {
            Exit();
        }

        private void OnHideUI() { uiHidden = true; }
        private void OnShowUI() { uiHidden = false; }

        private void OnCameraChange(CameraManager.CameraMode mode)
        {
            if (!active) return;
            if (mode != CameraManager.CameraMode.Flight)
            {
                Exit();
                return;
            }
            // Something switched KSP's camera back on; take it back.
            flightCamera.DeactivateUpdate();
        }

        private void Update()
        {
            bool togglePressed = TogglePressed();

            if (!active)
            {
                if (togglePressed && KeysAllowed() && CanEnter()) Enter();
                return;
            }

            if (togglePressed || !StillValid())
            {
                Exit();
                return;
            }

            if (FlightDriver.Pause)
            {
                SetCursorLocked(false);
                moveInput = default(MoveInput);
                return;
            }

            SetCursorLocked(true);
            // Stop KSP treating our clicks as world clicks (e.g. double-click to target a vessel).
            Mouse.Left.ClearMouseState();

            ReadMovementInput();
            UpdateLook();
            PlaceCamera();   // this frame's view, so shots go where the crosshair is now
            UpdateWeapon();
        }

        private void FixedUpdate()
        {
            if (!active || !StillValid() || FlightDriver.Pause) return;
            movement.Step(lookForward, Up(), moveInput, Time.fixedDeltaTime);
            // One-shot presses are consumed by the first physics step that sees them.
            moveInput.Jump = false;
            moveInput.SlidePressed = false;
        }

        private void LateUpdate()
        {
            if (!active || !StillValid()) return;

            // Re-assert our hold on the camera every frame; some KSP paths quietly switch it back on.
            if (flightCamera.updateActive) flightCamera.DeactivateUpdate();
            KeepCameraFrameCurrent();

            PlaceCamera();
            viewmodel.Animate(aim, reloading, movement.Sprinting && !movement.TacticalSprinting, movement.TacticalSprinting,
                movement.SlideBlend, movement.Grounded ? movement.HorizontalSpeed : 0f, Time.deltaTime);
        }

        // ---------------------------------------------------------------- entering / leaving

        private static bool TogglePressed()
        {
            if (!Input.GetKeyDown(Settings.ToggleKey)) return false;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return (!Settings.ToggleNeedsCtrl || ctrl) && (!Settings.ToggleNeedsShift || shift);
        }

        /// <summary>Not paused, not typing in a text box, no KSP dialog holding the controls.</summary>
        private static bool KeysAllowed()
        {
            return !FlightDriver.Pause
                && GUIUtility.keyboardControl == 0
                && InputLockManager.IsUnlocked(ControlTypes.EVA_INPUT);
        }

        private bool CanEnter()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.isEVA || MapView.MapIsEnabled || FlightCamera.fetch == null) return false;
            if (CameraManager.Instance != null && CameraManager.Instance.currentCameraMode != CameraManager.CameraMode.Flight) return false;

            kerbal = vessel.FindPartModuleImplementing<KerbalEVA>();
            if (kerbal == null || kerbal.fsm == null) return false;

            // Ladders, swimming off, ragdolls, the jetpack and construction mode all need KSP's own
            // EVA keys, which MW2 mode locks; only start from standing or walking on the ground.
            KFSMState s = kerbal.fsm.CurrentState;
            bool onGround = s == kerbal.st_idle_gr || s == kerbal.st_idle_b_gr || s == kerbal.st_land
                || s == kerbal.st_walk_acd || s == kerbal.st_walk_fps || s == kerbal.st_run_acd || s == kerbal.st_run_fps
                || s == kerbal.st_bound_gr_acd || s == kerbal.st_bound_gr_fps;
            if (!onGround || kerbal.InConstructionMode || kerbal.isRagdoll || kerbal.OnALadder || kerbal.JetpackDeployed)
            {
                ScreenMessages.PostScreenMessage("MW2 mode: stand on the ground first (jetpack off, not on a ladder)", 3f, ScreenMessageStyle.UPPER_CENTER);
                kerbal = null;
                return false;
            }
            return true;
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
            savedFov = flightCamera.FieldOfView;

            // If KSP's own mouse-look mode is on, switch it off so it doesn't fight ours for the cursor.
            if (CameraMouseLook.MouseLocked) CameraMouseLook.SetMouseLook(false);

            InputLockManager.SetControlLock(LockedControls, LockId);
            active = true;
            try
            {
                SetUp();
            }
            catch
            {
                // Never leave KSP's controls locked because something went wrong.
                Exit();
                throw;
            }
        }

        private void SetUp()
        {
            // Stop KSP's orbit camera from moving the camera; we place it ourselves every frame.
            flightCamera.DeactivateUpdate();
            KeepCameraFrameCurrent();
            flightCamera.SetFoV(Settings.FieldOfView);

            Vector3 up = Up();
            lookForward = Vector3.ProjectOnPlane(mainCamera.transform.forward, up);
            if (lookForward.sqrMagnitude < 1e-4f) lookForward = Vector3.ProjectOnPlane(kerbal.transform.forward, up);
            lookForward.Normalize();
            pitch = 0f;

            HideKerbal();
            movement = new CombatMovement(kerbal);
            viewmodel = new Viewmodel(mainCamera);
            audioSource = mainCamera.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            moveInput = default(MoveInput);
            tacticalLatched = false;
            speedFov = 0f;
            aim = 0f;
            bloom = 0f;
            reloading = false;
            hintUntil = Time.time + HintDuration;

            PlaceCamera();
        }

        private void Exit()
        {
            // Give KSP its controls back first, whatever else happens.
            InputLockManager.RemoveControlLock(LockId);
            if (cursorLockedByUs) SetCursorLocked(false);
            if (!active) return;
            active = false;

            if (movement != null) movement.Release();
            movement = null;

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

            CameraManager cameraManager = CameraManager.Instance;
            if (flightCamera != null)
            {
                bool inFlightView = cameraManager == null || cameraManager.currentCameraMode == CameraManager.CameraMode.Flight;
                if (inFlightView) flightCamera.SetFoV(savedFov);
                else flightCamera.FieldOfView = savedFov; // KSP reapplies it when returning to the flight camera
                flightCamera.ActivateUpdate();
            }
            // KSP may have snapshotted our zoomed FOV (e.g. when the map opened); make it restore the player's own.
            if (cameraManager != null && cameraManager.existingFlightFoV > 0f) cameraManager.existingFlightFoV = savedFov;

            reloading = false;
            kerbal = null;
        }

        private void KeepCameraFrameCurrent()
        {
            if (CameraFrameField != null) CameraFrameField.SetValue(flightCamera, FlightGlobals.GetFoR(FoRModes.SRF_NORTH));
        }

        private void HideKerbal()
        {
            hiddenRenderers.Clear();
            // KSP parents the camera pivot under the EVA Kerbal, so skip anything belonging to the camera.
            Transform pivot = flightCamera.GetPivot();
            foreach (Renderer r in kerbal.part.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || (pivot != null && r.transform.IsChildOf(pivot))) continue;
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

        private void SetCursorLocked(bool locked)
        {
            // Only ever release a cursor we locked, so KSP's own mouse-look isn't disturbed.
            if (!locked && !cursorLockedByUs) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            cursorLockedByUs = locked;
        }

        // ---------------------------------------------------------------- input and camera

        private void ReadMovementInput()
        {
            float x = (Input.GetKey(Settings.RightKey) ? 1f : 0f) - (Input.GetKey(Settings.LeftKey) ? 1f : 0f);
            float y = (Input.GetKey(Settings.ForwardKey) ? 1f : 0f) - (Input.GetKey(Settings.BackKey) ? 1f : 0f);
            moveInput.Move = new Vector2(x, y);
            moveInput.Crouch = Input.GetKey(Settings.CrouchKey) || Input.GetKey(Settings.CrouchKeyAlt);
            moveInput.Aiming = Input.GetMouseButton(1) && !reloading;
            moveInput.Firing = Input.GetMouseButton(0);

            // Sprint is hold-to-sprint. A quick double tap (tap, then press and hold) is a tactical sprint.
            bool sprintHeld = Input.GetKey(Settings.SprintKey);
            if (Input.GetKeyDown(Settings.SprintKey))
            {
                if (Time.time - lastSprintTapAt <= Settings.DoubleTapTime) tacticalLatched = true;
                lastSprintTapAt = Time.time;
            }
            // Firing, aiming, reloading, stopping or letting go of sprint all end a tactical sprint.
            if (!sprintHeld || y <= 0.1f || moveInput.Firing || moveInput.Aiming || reloading) tacticalLatched = false;

            // Firing or reloading drops you out of a sprint, like MW2.
            moveInput.Sprint = sprintHeld && !moveInput.Firing && !reloading;
            moveInput.TacticalSprint = tacticalLatched;

            if (Input.GetKeyDown(Settings.JumpKey)) moveInput.Jump = true;
            if (Input.GetKeyDown(Settings.CrouchKey) || Input.GetKeyDown(Settings.CrouchKeyAlt)) moveInput.SlidePressed = true;
        }

        /// <summary>"Up" for the controls: away from the centre of the body we're on.</summary>
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
            float slide = movement != null ? movement.SlideBlend : 0f;
            Quaternion look = Quaternion.LookRotation(lookForward, up) * Quaternion.Euler(pitch, 0f, Settings.SlideCameraTilt * slide);

            // Physics runs at a fixed rate; carry the body forward by its velocity since the last
            // physics step so the view glides instead of stepping at high frame rates.
            Rigidbody rb = kerbal.part.Rigidbody;
            Vector3 body = kerbal.transform.position;
            if (rb != null) body += rb.velocity * (Time.time - Time.fixedTime);

            float eyeHeight = Settings.EyeHeight - Settings.CrouchEyeDrop * (movement != null ? movement.Crouch : 0f)
                - Settings.SlideEyeDrop * slide;
            Vector3 eye = body + up * eyeHeight;

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
            bool fast = movement.TacticalSprinting || movement.Sliding;
            speedFov = Mathf.MoveTowards(speedFov, fast ? 1f : 0f, dt * 4f);
            flightCamera.SetFoV(Mathf.Lerp(Settings.FieldOfView, Settings.AimFieldOfView, aim) + Settings.SprintFovBoost * speedFov * (1f - aim));
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

            // The rifle has to come down out of the sprint pose before it can fire.
            if (Input.GetMouseButton(0) && Time.time >= nextShotAt && viewmodel.ReadyToFire)
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
            if (!movement.Grounded || movement.HorizontalSpeed > Settings.WalkSpeed * 0.5f) spread *= 1.5f;
            Vector2 offset = Random.insideUnitCircle * spread;
            Vector3 direction = cam.rotation * (Quaternion.Euler(offset.y, offset.x, 0f) * Vector3.forward);

            RaycastHit hit;
            bool didHit = FindHit(cam.position, direction, out hit);
            viewmodel.Fire(didHit ? hit.point : cam.position + direction * Settings.Range);
            Play(Sounds.Gunshot, 1f);

            // Recoil: kick the view up, bloom the spread, and push the shooter back.
            pitch = Mathf.Clamp(pitch - Settings.Recoil * Mathf.Lerp(1f, 0.5f, aim), -89f, 89f);
            lookForward = Quaternion.AngleAxis(Random.Range(-0.3f, 0.3f) * Settings.Recoil, Up()) * lookForward;
            bloom = Mathf.Min(bloom + 0.4f * (1f - 0.7f * aim), 3f);
            Rigidbody self = kerbal.part.Rigidbody;
            if (self != null) self.AddForce(-direction * Settings.RecoilImpulse, ForceMode.Impulse);

            if (!didHit) return;
            Shapes.Impact(hit.point, hit.normal);

            Part part = hit.collider.GetComponentInParent<Part>();
            if (part == null) return;

            bool isKerbal = Damage.IsKerbal(part);
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

            if (aim < 0.9f && !movement.Sprinting && viewmodel.ReadyToFire)
            {
                float spread = Mathf.Lerp(Settings.HipSpread, Settings.AimSpread, aim) + bloom;
                DrawCrosshair(cx, cy, 8f + spread * 8f, new Color(1f, 1f, 1f, 0.85f * (1f - aim)));
            }
            else if (aim >= 0.9f)
            {
                DrawRect(cx - 1.5f, cy - 1.5f, 3f, 3f, new Color(1f, 0.2f, 0.2f, 0.9f));
            }

            if (Time.time < hitmarkerUntil) DrawHitmarker(cx, cy, hitmarkerKill ? new Color(1f, 0.25f, 0.2f) : Color.white);
            if (uiHidden) return; // F2: keep just the crosshair, like the rest of KSP's UI

            if (movement.Ragdolled)
                ShadowLabel(new Rect(0f, cy + 60f, Screen.width, 30f), "Knocked down - press WASD or Space to get up", hintText);

            float right = Screen.width - 40f;
            float bottom = Screen.height - 40f;
            ShadowLabel(new Rect(right - 300f, bottom - 90f, 300f, 50f), reloading ? "RELOADING" : ammo + " / " + Settings.MagazineSize, bigText);
            ShadowLabel(new Rect(right - 300f, bottom - 40f, 300f, 30f), "Kerbals: " + kerbalKills + "   Parts: " + partsDestroyed, smallText);

            if (Time.time < hintUntil)
            {
                string toggle = (Settings.ToggleNeedsCtrl ? "Ctrl+" : "") + (Settings.ToggleNeedsShift ? "Shift+" : "") + Settings.ToggleKey;
                ShadowLabel(new Rect(0f, 50f, Screen.width, 30f), "MW2 MODE  -  KSP controls are off.  [" + toggle + "] to exit", hintText);
                ShadowLabel(new Rect(0f, 78f, Screen.width, 30f),
                    "WASD move   Shift sprint (double-tap: tactical)   Space jump   " + Settings.CrouchKey + " crouch (while sprinting: slide)   LMB fire   RMB aim   " + Settings.ReloadKey + " reload", hintText);
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
