using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HandsOff
{
    public sealed class RepulsionInput : MonoBehaviour
    {
        [SerializeField, Range(0.5f, 1f)] private float fatalRadiusScale = 0.85f;
        [SerializeField, Min(0.1f)] private float fieldReach = 2.4f;
        [SerializeField, Min(0f)] private float burstImpulse = 7.8f;
        [SerializeField, Min(0f)] private float holdForce = 18f;
        [SerializeField, Min(0.1f)] private float speedCap = 9f;

        private readonly Queue<Vector2> bursts = new Queue<Vector2>(4);
        private readonly List<RaycastResult> hits = new List<RaycastResult>(8);
        private PointerEventData uiPointer;
        private struct Contact
        {
            public int id;
            public bool live, ignored;
            public Vector2 relative;
        }
        private Contact[] otherContacts = System.Array.Empty<Contact>();
        private Arena2D arena;
        private GameView view;
        private HazardDirector hazards;
        private Camera worldCamera;
        private ButtonControl press;
        private Vector2Control position;
        private TouchControl touch;
        private Vector2 worldPoint, previousRelative;
        private bool waitForRelease;
        private bool ignoredGesture;
        private int ignoreFrame;

        public bool NewGestureThisFrame { get; private set; }

        public void Initialize(Arena2D playfield, GameView gameView, Camera camera, HazardDirector hazardDirector)
        {
            arena = playfield;
            view = gameView;
            worldCamera = camera;
            hazards = hazardDirector;
        }

        public void ResetGesture()
        {
            press = null;
            position = null;
            touch = null;
            ignoredGesture = false;
            bursts.Clear();
            System.Array.Clear(otherContacts, 0, otherContacts.Length);
            ignoreFrame = Time.frameCount;
            waitForRelease = AnyDown();
            view.SetField(false, Vector2.zero, fieldReach);
        }

        private static bool AnyDown()
        {
            var screen = Touchscreen.current;
            if (screen != null)
                foreach (var finger in screen.touches)
                    if (finger.press.isPressed) return true;
            var pointer = NonTouchPointer();
            return pointer != null && pointer.press.isPressed;
        }

        private static Pointer NonTouchPointer()
        {
            var pointer = Pointer.current;
            return pointer != null && !(pointer is Touchscreen) ? pointer : Mouse.current;
        }

        private bool CapturePress()
        {
            var screen = Touchscreen.current;
            if (screen != null)
            {
                foreach (var finger in screen.touches)
                {
                    if (!finger.press.wasPressedThisFrame) continue;
                    touch = finger;
                    press = finger.press;
                    position = finger.position;
                    return true;
                }
            }
            var pointer = NonTouchPointer();
            if (pointer == null || !pointer.press.wasPressedThisFrame) return false;
            press = pointer.press;
            position = pointer.position;
            return true;
        }

        private bool OverUi(Vector2 screenPoint, int pointerId)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            if (pointerId >= 0 && events.IsPointerOverGameObject(pointerId)) return true;
            if (pointerId < 0 && events.IsPointerOverGameObject()) return true;
            // Also catch a down/up inside one rendered frame, after the UI released its pointer.
            if (uiPointer == null) uiPointer = new PointerEventData(events);
            uiPointer.Reset();
            uiPointer.position = screenPoint;
            hits.Clear();
            events.RaycastAll(uiPointer, hits);
            return hits.Count != 0;
        }

        // Only the controlling finger pushes. Other gameplay fingers can still break the core.
        private bool OtherFingerHit()
        {
            var screen = Touchscreen.current;
            if (screen == null) return false;
            if (otherContacts.Length != screen.touches.Count) otherContacts = new Contact[screen.touches.Count];
            for (int i = 0; i < otherContacts.Length; i++)
            {
                var finger = screen.touches[i];
                ref Contact contact = ref otherContacts[i];
                bool down = finger.press.isPressed;
                if (finger == touch || (!down && !finger.press.wasReleasedThisFrame) ||
                    finger.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    contact.live = false;
                    continue;
                }
                int id = finger.touchId.ReadValue();
                if (!contact.live || contact.id != id)
                {
                    if (!down && !finger.press.wasPressedThisFrame) continue;
                    Vector2 start = finger.startPosition.ReadValue();
                    contact.id = id;
                    contact.ignored = OverUi(start, id);
                    contact.relative = ArenaPoint(start) - arena.VisibleCorePosition;
                }
                Vector2 current = ArenaPoint(finger.position.ReadValue()) - arena.VisibleCorePosition;
                if (!contact.ignored && Arena2D.DistanceToSegment(Vector2.zero, contact.relative, current)
                    <= arena.CoreRadius * fatalRadiusScale) return true;
                contact.relative = current;
                contact.live = down;
            }
            return false;
        }

        public bool Sample()
        {
            NewGestureThisFrame = false;
            if (Time.frameCount == ignoreFrame) return false;
            if (waitForRelease)
            {
                waitForRelease = AnyDown();
                return false;
            }
            bool fresh = press == null;
            if (fresh) CapturePress();
            if (OtherFingerHit()) return true;
            if (press == null) return false;
            if (!press.device.added || (touch != null &&
                touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled))
            {
                ResetGesture();
                return false;
            }

            Vector2 screenPoint = position.ReadValue();
            Vector2 initial = fresh && touch != null ? touch.startPosition.ReadValue() : screenPoint;
            if (fresh) ignoredGesture = OverUi(initial, touch != null ? touch.touchId.ReadValue() : -1);
            if (ignoredGesture)
            {
                if (!press.isPressed)
                {
                    press = null;
                    position = null;
                    touch = null;
                }
                return false;
            }
            worldPoint = ArenaPoint(screenPoint);
            Vector2 start = fresh ? ArenaPoint(initial) : worldPoint;
            Vector2 core = arena.VisibleCorePosition;
            Vector2 relative = worldPoint - core;
            if (fresh) previousRelative = start - core;
            if (Arena2D.DistanceToSegment(Vector2.zero, previousRelative, relative) <= arena.CoreRadius * fatalRadiusScale)
                return true;

            if (fresh)
            {
                NewGestureThisFrame = true;
                bursts.Enqueue(start);
                view.OnFirstPush();
                view.Pulse(start, GameView.Ink, 0.18f, fieldReach * 2f);
                view.PlaySound(GameView.Sound.Push);
            }
            previousRelative = relative;
            bool down = press.isPressed;
            view.SetField(down, worldPoint, fieldReach);
            if (!down)
            {
                press = null;
                position = null;
                touch = null;
            }
            return false;
        }

        private Vector2 ArenaPoint(Vector2 pixel)
        {
            // Camera shake is presentation only.
            return (Vector2)worldCamera.ScreenToWorldPoint(pixel) - view.CameraOffset;
        }

        public bool TryGetHeldPointer(out Vector2 point)
        {
            point = worldPoint;
            return press != null && press.device.added && press.isPressed && !ignoredGesture;
        }

        public void ApplyPhysics()
        {
            while (bursts.Count > 0)
                Push(bursts.Dequeue(), burstImpulse, ForceMode2D.Impulse);
            if (press != null && press.isPressed && !ignoredGesture)
                Push(worldPoint, holdForce, ForceMode2D.Force);
            arena.Body.linearVelocity = Vector2.ClampMagnitude(arena.Body.linearVelocity, speedCap * arena.SpeedMultiplier);
        }

        private void Push(Vector2 origin, float strength, ForceMode2D mode)
        {
            Vector2 force = RepulsionForce(arena.Body.position - origin, strength * arena.SpeedMultiplier, fieldReach);
            if (force.sqrMagnitude > 0f)
            {
                arena.Body.AddForce(force, mode);
                if (mode == ForceMode2D.Impulse) arena.ReactToPush(force);
            }
            hazards.RepelMines(origin, strength, fieldReach, mode);
        }

        internal static Vector2 RepulsionForce(Vector2 away, float strength, float reach)
        {
            float distance = away.magnitude;
            if (distance >= reach || distance < 0.0001f) return Vector2.zero;
            float falloff = (1f - distance / reach) / (0.55f + distance);
            return away / distance * (strength * falloff);
        }
    }
}
