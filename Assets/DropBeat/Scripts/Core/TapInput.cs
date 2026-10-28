using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DropBeat
{
    /// <summary>
    /// Collects every tap with the Input System's event timestamp (not the frame time),
    /// so a press is judged at the moment it happened even if the frame was long.
    /// Any of Space / D F J K / Z X / mouse / touch / gamepad A works.
    /// </summary>
    public sealed class TapInput : System.IDisposable
    {
        public struct Tap
        {
            /// <summary>Time on the Time.realtimeSinceStartupAsDouble timeline.</summary>
            public double realtime;
            public Vector2 screenPosition;
            public bool isPointer;
        }

        private readonly InputAction _action;
        private readonly Queue<Tap> _queue = new Queue<Tap>(16);
        private readonly HashSet<InputControl> _down = new HashSet<InputControl>();

        public TapInput()
        {
            _action = new InputAction("Tap", InputActionType.PassThrough);
            string[] paths =
            {
                "<Keyboard>/space", "<Keyboard>/d", "<Keyboard>/f", "<Keyboard>/j", "<Keyboard>/k",
                "<Keyboard>/z", "<Keyboard>/x", "<Mouse>/leftButton", "<Touchscreen>/touch*/press",
                "<Gamepad>/buttonSouth", "<Gamepad>/rightTrigger", "<Gamepad>/leftTrigger"
            };
            foreach (string p in paths) _action.AddBinding(p);
            _action.performed += OnPerformed;
            _action.Enable();
        }

        public bool Enabled { get; set; } = true;

        private void OnPerformed(InputAction.CallbackContext ctx)
        {
            InputControl control = ctx.control;
            bool pressed = ctx.ReadValue<float>() >= 0.5f;
            if (!pressed)
            {
                _down.Remove(control);
                return;
            }

            if (!_down.Add(control)) return;
            if (!Enabled) return;

            double now = Time.realtimeSinceStartupAsDouble;
            double t = ctx.time;
            double lag = now - t;
            if (lag < -0.05 || lag > 1.0) t = now; // survives long hitches; rejects nonsense stamps

            var tap = new Tap { realtime = t, screenPosition = new Vector2(float.NaN, float.NaN) };
            if (control.device is Mouse mouse)
            {
                tap.isPointer = true;
                tap.screenPosition = mouse.position.ReadValue();
            }
            else if (control.parent is TouchControl touch)
            {
                tap.isPointer = true;
                tap.screenPosition = touch.position.ReadValue();
            }

            _queue.Enqueue(tap);
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
            _action.performed -= OnPerformed;
            _action.Disable();
            _action.Dispose();
        }
    }
}
