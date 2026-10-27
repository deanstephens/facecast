using UnityEngine;
using UnityEngine.InputSystem;

namespace Facecast
{
    /// <summary>
    /// You, in first person. Click to capture the mouse and look around (Esc lets go), WASD or the arrow keys to walk,
    /// Shift to run, Space to jump; M and V switch your microphone and camera, Z the zoom on your face and P sharing
    /// your screen (in the web build; offline, P puts up a demo projection). Sticky notes are <see cref="NoteHand"/>'s.
    /// Your own avatar is invisible to you but casts its shadow. Your pose goes to the room 15 times a second. Before
    /// you join, the camera circles the square slowly behind the page's join card.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayer : MonoBehaviour
    {
        public const float EyeHeight = 1.5f;
        const float SendInterval = 1f / 15f;
        const float MaxLookStep = 250f;   // pixels per frame: browsers' pointer lock sometimes reports wild jumps

        public Camera Cam;
        public GameObject AvatarPrefab;
        public float WalkSpeed = 3.4f;
        public float RunSpeed = 6.2f;
        public float JumpSpeed = 4.8f;
        public float Gravity = 15f;
        public float LookDegreesPerPixel = 0.11f;

        public bool Spawned { get; private set; }
        /// <summary>Off while the screenshot tour moves the camera.</summary>
        public bool InputEnabled { get; set; } = true;
        /// <summary>A note is open to write on: the keys are for that, so you stand still.</summary>
        public bool Typing { get; set; }
        public AvatarRig Avatar { get; private set; }
        public AvatarPose Pose => new(transform.position, _yaw, _pitch);
        /// <summary>How you actually moved last frame (walls stop you), for your avatar's lean and stride.</summary>
        public Vector3 Velocity => _moved;
        public bool Grounded => _cc.isGrounded;

        CharacterController _cc;
        InputAction _move, _look, _run, _jump, _click, _mic, _cam, _zoom, _share;
        float _yaw, _pitch, _nextSend;
        Vector3 _velocity, _moved;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");
            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _run = new InputAction("Run", InputActionType.Button, "<Keyboard>/leftShift");
            _run.AddBinding("<Keyboard>/rightShift");
            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");
            _click = new InputAction("Capture", InputActionType.Button, "<Mouse>/leftButton");
            _mic = new InputAction("Microphone", InputActionType.Button, "<Keyboard>/m");
            _cam = new InputAction("Camera", InputActionType.Button, "<Keyboard>/v");
            _zoom = new InputAction("Face zoom", InputActionType.Button, "<Keyboard>/z");
            _share = new InputAction("Share screen", InputActionType.Button, "<Keyboard>/p");
        }

        void OnEnable()
        {
            foreach (var a in new[] { _move, _look, _run, _jump, _click, _mic, _cam, _zoom, _share }) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in new[] { _move, _look, _run, _jump, _click, _mic, _cam, _zoom, _share }) a.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in new[] { _move, _look, _run, _jump, _click, _mic, _cam, _zoom, _share }) a.Dispose();
        }

        /// <summary>Puts you in the square (the page said you joined), wearing <paramref name="color"/>.</summary>
        public void Spawn(AvatarPose pose, Color color)
        {
            if (!Avatar)
            {
                Avatar = Instantiate(AvatarPrefab, transform).GetComponent<AvatarRig>();
                Avatar.name = "Your Avatar";
                Avatar.transform.localPosition = Vector3.zero;
                Avatar.SetShadowsOnly(true);
            }
            Avatar.SetColor(color);
            Cam.transform.SetParent(transform, false);
            Cam.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            Spawned = true;
            Teleport(pose);
        }

        public void Teleport(AvatarPose pose)
        {
            _cc.enabled = false;
            transform.position = pose.Position;
            _cc.enabled = true;
            _velocity = _moved = Vector3.zero;
            _yaw = pose.Yaw;
            _pitch = pose.Pitch;
            ApplyLook();
            _nextSend = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Spawned)
            {
                Orbit();
                return;
            }
            if (InputEnabled && Typing) Step(Vector2.zero, Vector2.zero, false, false, dt);
            else if (InputEnabled)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                if (_click.WasPressedThisFrame() && !locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;  // the browser asks for pointer lock on the next click
                    Cursor.visible = false;
                }
                if (_mic.WasPressedThisFrame()) WebBridge.ToggleMic();
                if (_cam.WasPressedThisFrame()) WebBridge.ToggleCam();
                if (_zoom.WasPressedThisFrame()) WebBridge.ToggleZoom();
                if (_share.WasPressedThisFrame())
                {
                    if (WebBridge.Available) WebBridge.ToggleShare();
                    else if (Projections.Instance) Projections.Instance.ToggleDemo();
                }
                var look = locked ? Vector2.ClampMagnitude(_look.ReadValue<Vector2>(), MaxLookStep) : Vector2.zero;
                Step(_move.ReadValue<Vector2>(), look, _run.IsPressed(), _jump.WasPressedThisFrame(), dt);
            }
            if (Avatar) Avatar.Animate(transform.InverseTransformDirection(_moved), _pitch, 0f, dt);

            float now = Time.realtimeSinceStartup;
            if (now >= _nextSend)
            {
                _nextSend = now + SendInterval;
                WebBridge.SendPose(Pose, now);
            }
        }

        /// <summary>One frame of movement: <paramref name="move"/> from the keys (x right, y forward), <paramref name="lookDelta"/> in pixels.</summary>
        public void Step(Vector2 move, Vector2 lookDelta, bool run, bool jump, float dt)
        {
            _yaw = Mathf.Repeat(_yaw + lookDelta.x * LookDegreesPerPixel, 360f);
            _pitch = Mathf.Clamp(_pitch - lookDelta.y * LookDegreesPerPixel, -85f, 85f);
            ApplyLook();

            move = Vector2.ClampMagnitude(move, 1f);
            var wish = transform.right * move.x + transform.forward * move.y;
            float speed = run ? RunSpeed : WalkSpeed;
            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            // Quick to start and stop, slower to turn in the air.
            float accel = _cc.isGrounded ? 14f : 3f;
            horizontal = Vector3.Lerp(horizontal, wish * speed, 1f - Mathf.Exp(-accel * dt));

            float vy = _velocity.y;
            if (_cc.isGrounded)
            {
                vy = -2f;  // keep pressed to the ground down small steps
                if (jump) vy = JumpSpeed;
            }
            vy -= Gravity * dt;
            // The velocity you're trying to move at carries over between frames; feeding back what actually
            // happened instead would stall: at high frame rates the first steps are shorter than the controller's
            // minimum move, so nothing moves, and the speed would never build up.
            _velocity = new Vector3(horizontal.x, vy, horizontal.z);
            var before = transform.position;
            var hit = _cc.Move(_velocity * dt);
            if ((hit & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f;  // bumped your head
            _moved = (transform.position - before) / Mathf.Max(dt, 1e-4f);
        }

        void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (Spawned && Cam) Cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>Before you join: a slow circle around the square at head height of a tall tree.</summary>
        void Orbit()
        {
            if (!Cam) return;
            float a = Time.time * 0.05f + 0.6f;
            var pos = new Vector3(Mathf.Sin(a) * 15.5f, 4.2f, Mathf.Cos(a) * 15.5f);
            Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(0f, 1.3f, 0f) - pos));
        }
    }
}
