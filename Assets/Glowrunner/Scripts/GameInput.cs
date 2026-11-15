using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace Glowrunner
{
    /// <summary>Keyboard (and gamepad) input that works with either the legacy Input Manager or the Input System.</summary>
    public static class GameInput
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2Int ReadDirection()
        {
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) return Dir.Up;
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) return Dir.Down;
                if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) return Dir.Left;
                if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) return Dir.Right;
                if (k.upArrowKey.isPressed || k.wKey.isPressed) return Dir.Up;
                if (k.downArrowKey.isPressed || k.sKey.isPressed) return Dir.Down;
                if (k.leftArrowKey.isPressed || k.aKey.isPressed) return Dir.Left;
                if (k.rightArrowKey.isPressed || k.dKey.isPressed) return Dir.Right;
            }
            var g = Gamepad.current;
            if (g != null) return FromAxes(g.leftStick.ReadValue() + g.dpad.ReadValue());
            return Vector2Int.zero;
        }

        public static bool ConfirmPressed()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame))
                || (g != null && g.startButton.wasPressedThisFrame)
                || (g != null && g.buttonSouth.wasPressedThisFrame);
        }

        public static bool PausePressed()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            return (k != null && (k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
                || (g != null && g.startButton.wasPressedThisFrame);
        }

        public static bool MutePressed()
        {
            var k = Keyboard.current;
            return k != null && k.mKey.wasPressedThisFrame;
        }

        /// <summary>-1 / +1 when left / right was pressed this frame (menus), otherwise 0.</summary>
        public static int MenuStep()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            if ((k != null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame)) || (g != null && g.dpad.left.wasPressedThisFrame)) return -1;
            if ((k != null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame)) || (g != null && g.dpad.right.wasPressedThisFrame)) return 1;
            return 0;
        }

        public static bool CameraTogglePressed()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            return (k != null && k.cKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame);
        }
#else
        public static Vector2Int ReadDirection()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) return Dir.Up;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) return Dir.Down;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) return Dir.Left;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return Dir.Right;
            return FromAxes(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")));
        }

        public static bool ConfirmPressed() =>
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
            || Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton7);

        public static bool PausePressed() =>
            Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape);

        public static bool MutePressed() => Input.GetKeyDown(KeyCode.M);

        /// <summary>-1 / +1 when left / right was pressed this frame (menus), otherwise 0.</summary>
        public static int MenuStep()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) return -1;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return 1;
            return 0;
        }

        public static bool CameraTogglePressed() =>
            Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.JoystickButton3);
#endif

        static Vector2Int FromAxes(Vector2 v)
        {
            if (v.sqrMagnitude < 0.25f) return Vector2Int.zero;
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) return v.x > 0 ? Dir.Right : Dir.Left;
            return v.y > 0 ? Dir.Up : Dir.Down;
        }
    }
}
