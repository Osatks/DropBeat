using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DropBeat
{
    public enum TapSide : byte
    {
        Any = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>
    /// Gameplay input with Input System event timestamps (not frame times), so judging is exact even
    /// on a long frame. Produces presses, releases (hold notes) and swipes (touch/mouse drags, arrow keys,
    /// d-pad). A swipe carries the time of the finger going down, which is the moment that is judged.
    /// Left/right is known per press: screen half for touch/mouse, D F Z / J K X for keys, triggers on pads.
    /// </summary>
    public sealed class TapInput : System.IDisposable
    {
        public enum Kind : byte { Press, Release, Swipe }

        public struct Tap
        {
            public Kind kind;
            /// <summary>Time on the Time.realtimeSinceStartupAsDouble timeline.</summary>
            public double realtime;
            public Vector2 screenPosition;
            public bool isPointer;
            public TapSide side;
            /// <summary>Identifies the finger / key so a release can be matched to its press.</summary>
            public int id;
            public SwipeDir swipe;
        }

        private struct Gesture
        {
            public int id;
            public Vector2 start;
            public double startTime;
            public bool swiped;
            public TouchControl touch;
            public bool mouse;
        }

        private readonly InputAction _press;
        private readonly InputAction _swipeKeys;
        private readonly Queue<Tap> _queue = new Queue<Tap>(32);
        private readonly HashSet<InputControl> _down = new HashSet<InputControl>();
        private readonly List<Gesture> _gestures = new List<Gesture>(4);

        /// <summary>Drag distance (fraction of the shorter screen side) that counts as a swipe.</summary>
        public float swipeThreshold = 0.07f;
        /// <summary>A drag has to reach the threshold within this many seconds of touching down.</summary>
        public float swipeMaxTime = 0.35f;

        public TapInput()
        {
            _press = new InputAction("Press", InputActionType.PassThrough);
            string[] paths =
            {
                "<Keyboard>/space", "<Keyboard>/d", "<Keyboard>/f", "<Keyboard>/j", "<Keyboard>/k",
                "<Keyboard>/z", "<Keyboard>/x", "<Mouse>/leftButton", "<Touchscreen>/touch*/press",
                "<Gamepad>/buttonSouth", "<Gamepad>/rightTrigger", "<Gamepad>/leftTrigger",
                "<Gamepad>/leftShoulder", "<Gamepad>/rightShoulder"
            };
            foreach (string p in paths) _press.AddBinding(p);
            _press.performed += OnPress;
            _press.Enable();

            _swipeKeys = new InputAction("SwipeKeys", InputActionType.PassThrough);
            string[] swipePaths =
            {
                "<Keyboard>/leftArrow", "<Keyboard>/rightArrow", "<Keyboard>/upArrow",
                "<Gamepad>/dpad/left", "<Gamepad>/dpad/right", "<Gamepad>/dpad/up"
            };
            foreach (string p in swipePaths) _swipeKeys.AddBinding(p);
            _swipeKeys.performed += OnSwipeKey;
            _swipeKeys.Enable();
        }

        public bool Enabled { get; set; } = true;

        private static double Stamp(InputAction.CallbackContext ctx)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double t = ctx.time;
            double lag = now - t;
            if (lag < -0.05 || lag > 1.0) t = now; // survives long hitches; rejects nonsense stamps
            return t;
        }

        private void OnPress(InputAction.CallbackContext ctx)
        {
            InputControl control = ctx.control;
            bool pressed = ctx.ReadValue<float>() >= 0.5f;
            int id = control.GetHashCode();
            double t = Stamp(ctx);

            if (!pressed)
            {
                if (_down.Remove(control))
                {
                    _gestures.RemoveAll(g => g.id == id);
                    if (Enabled) _queue.Enqueue(new Tap { kind = Kind.Release, realtime = t, id = id });
                }
                return;
            }

            if (!_down.Add(control)) return;
            if (!Enabled) return;

            var tap = new Tap { kind = Kind.Press, realtime = t, id = id, screenPosition = new Vector2(float.NaN, float.NaN) };
            if (control.device is Mouse mouse)
            {
                tap.isPointer = true;
                tap.screenPosition = mouse.position.ReadValue();
                _gestures.Add(new Gesture { id = id, start = tap.screenPosition, startTime = t, mouse = true });
            }
            else if (control.parent is TouchControl touch)
            {
                tap.isPointer = true;
                tap.screenPosition = touch.position.ReadValue();
                _gestures.Add(new Gesture { id = id, start = tap.screenPosition, startTime = t, touch = touch });
            }

            tap.side = SideOf(control, tap);
            _queue.Enqueue(tap);
        }

        private void OnSwipeKey(InputAction.CallbackContext ctx)
        {
            if (!Enabled || ctx.ReadValue<float>() < 0.5f) return;
            InputControl c = ctx.control;
            if (!_down.Add(c)) return;
            string n = c.name.ToLowerInvariant();
            SwipeDir dir = n.Contains("left") ? SwipeDir.Left : n.Contains("right") ? SwipeDir.Right : SwipeDir.Up;
            _queue.Enqueue(new Tap { kind = Kind.Swipe, realtime = Stamp(ctx), id = c.GetHashCode(), swipe = dir, screenPosition = new Vector2(float.NaN, float.NaN) });
            // Arrow keys have no "held" state for holds; release them from the down set next press cycle.
            _down.Remove(c);
        }

        private static TapSide SideOf(InputControl control, Tap tap)
        {
            if (tap.isPointer) return tap.screenPosition.x < Screen.width * 0.5f ? TapSide.Left : TapSide.Right;
            string n = control.name.ToLowerInvariant();
            switch (n)
            {
                case "d": case "f": case "z": case "lefttrigger": case "leftshoulder": return TapSide.Left;
                case "j": case "k": case "x": case "righttrigger": case "rightshoulder": return TapSide.Right;
                default: return TapSide.Any;
            }
        }

        /// <summary>Checks active drags for swipes. Call once per frame before reading the queue.</summary>
        public void Poll()
        {
            if (_gestures.Count == 0) return;
            float threshold = Mathf.Min(Screen.width, Screen.height) * swipeThreshold;
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = _gestures.Count - 1; i >= 0; i--)
            {
                Gesture g = _gestures[i];
                if (g.swiped) continue;
                if (now - g.startTime > swipeMaxTime)
                {
                    g.swiped = true;
                    _gestures[i] = g;
                    continue;
                }

                Vector2 pos = g.mouse
                    ? (Mouse.current != null ? Mouse.current.position.ReadValue() : g.start)
                    : (g.touch != null ? g.touch.position.ReadValue() : g.start);
                Vector2 d = pos - g.start;
                if (d.magnitude < threshold) continue;

                SwipeDir dir = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x < 0f ? SwipeDir.Left : SwipeDir.Right) : (d.y > 0f ? SwipeDir.Up : SwipeDir.None);
                g.swiped = true;
                _gestures[i] = g;
                if (dir != SwipeDir.None && Enabled)
                {
                    _queue.Enqueue(new Tap { kind = Kind.Swipe, realtime = g.startTime, id = g.id, swipe = dir, screenPosition = g.start, isPointer = true });
                }
            }
        }

        public bool TryDequeue(out Tap tap)
        {
            if (_queue.Count > 0)
            {
                tap = _queue.Dequeue();
                return true;
            }

            tap = default;
            return false;
        }

        public void Clear()
        {
            _queue.Clear();
        }

        public void Dispose()
        {
            _press.performed -= OnPress;
            _press.Disable();
            _press.Dispose();
            _swipeKeys.performed -= OnSwipeKey;
            _swipeKeys.Disable();
            _swipeKeys.Dispose();
        }
    }
}
