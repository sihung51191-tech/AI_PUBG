using Aimmy2.Class;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Class;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using MouseMovementLibraries.SendInputSupport;
using System.Drawing;
using System.Runtime.InteropServices;
using Aimmy2.AILogic;

namespace InputLogic
{
    internal class MouseManager
    {
        private static double ScreenWidth => DisplayManager.ScreenWidth;
        private static double ScreenHeight => DisplayManager.ScreenHeight;

        private static DateTime LastClickTime = DateTime.MinValue;
        private static bool isSpraying = false;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private static double previousX = 0;
        private static double previousY = 0;
        public static double smoothingFactor = 0.5;
        public static bool IsEMASmoothingEnabled = false;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private static Random MouseRandom = new();

        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        // Cleanup
        private static (Action down, Action up) GetMouseActions()
        {
            string mouseMovementMethod = Dictionary.dropdownState["Mouse Movement Method"];
            Action mouseDownAction;
            Action mouseUpAction;

            switch (mouseMovementMethod)
            {
                case "SendInput":
                    mouseDownAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTDOWN);
                    mouseUpAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTUP);
                    break;
                case "LG HUB":
                    mouseDownAction = () => LGMouse.Move(1, 0, 0, 0);
                    mouseUpAction = () => LGMouse.Move(0, 0, 0, 0);
                    break;
                case "Razer Synapse (Require Razer Peripheral)":
                    mouseDownAction = () => RZMouse.mouse_click(1);
                    mouseUpAction = () => RZMouse.mouse_click(0);
                    break;
                case "ddxoft Virtual Input Driver":
                    mouseDownAction = () => DdxoftMain.Button(1);
                    mouseUpAction = () => DdxoftMain.Button(2);
                    break;
                default:
                    mouseDownAction = () => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouseUpAction = () => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
            }

            return (mouseDownAction, mouseUpAction);
        }

        public static void DoTriggerClick(RectangleF? detectionBox = null)
        {
            // NEW: Only trigger if the toggle is ON and the specific key is held.
            if (!Dictionary.toggleState["Auto Trigger"] || !InputBindingManager.IsHoldingBinding("Auto Click Keybind"))
            {
                ResetSprayState();
                return;
            }


            if (Dictionary.toggleState["Spray Mode"])
            {
                if (Dictionary.toggleState["Cursor Check"])
                {
                    Point mousePos = WinAPICaller.GetCursorPosition();

                    if (detectionBox.HasValue && !detectionBox.Value.Contains(mousePos.X, mousePos.Y))
                    {
                        if (isSpraying) ReleaseMouseButton();
                        return;
                    }
                }

                if (!isSpraying) HoldMouseButton();
                return;
            }

            // Single click logic if spray mode off
            int timeSinceLastClick = (int)(DateTime.UtcNow - LastClickTime).TotalMilliseconds;
            int triggerDelayMilliseconds = (int)(Dictionary.sliderSettings["Auto Trigger Delay"] * 1000);
            const int clickDelayMilliseconds = 20;

            if (timeSinceLastClick < triggerDelayMilliseconds && LastClickTime != DateTime.MinValue)
            {
                return;
            }

            var (mouseDown, mouseUp) = GetMouseActions();

            mouseDown.Invoke();
            Task.Run(async () =>
            {
                await Task.Delay(clickDelayMilliseconds);
                mouseUp.Invoke();
            });

            LastClickTime = DateTime.UtcNow;
        }

        #region Spray Mode Methods
        public static void HoldMouseButton()
        {
            if (isSpraying) return;

            var (mouseDown, _) = GetMouseActions();
            mouseDown.Invoke();
            isSpraying = true;
        }

        public static void ReleaseMouseButton()
        {
            if (!isSpraying) return;

            var (_, mouseUp) = GetMouseActions();
            mouseUp.Invoke();
            isSpraying = false;
        }

        public static void ResetSprayState()
        {
            if (isSpraying)
            {
                ReleaseMouseButton();
            }
        }
        #endregion

        public static void MoveCrosshair(int detectedX, int detectedY, double? frameDeltaSeconds = null)
        {
            int halfScreenWidth = (int)ScreenWidth / 2;
            int halfScreenHeight = (int)ScreenHeight / 2;

            int targetX = detectedX - halfScreenWidth;
            int targetY = detectedY - halfScreenHeight;

            double aspectRatioCorrection = ScreenWidth / ScreenHeight;

            int ActiveSlot = AIManager.ActiveSlot; // Ensure AIManager.ActiveSlot is accessible

            double sensitivity = Other.MouseSensitivityProfiles.Get(ActiveSlot);
            // Keep the existing response below .99 and positive damping at 1 without
            // allowing (1 - sensitivity) to become negative and reverse movement.
            sensitivity = Math.Clamp(sensitivity, 0.01, 1.0);
            if (sensitivity >= 0.99) sensitivity = 1.0 - 0.01 / (1.0 + sensitivity - 0.99);
            int MouseJitter = (int)Dictionary.sliderSettings[ActiveSlot == 1 ? "Slot 1 Mouse Jitter" : "Mouse Jitter"];
            
            int jitterX = MouseRandom.Next(-MouseJitter, MouseJitter);
            int jitterY = MouseRandom.Next(-MouseJitter, MouseJitter);

            Point start = new(0, 0);
            Point end = new(targetX, targetY);
            Point newPosition = new Point(0, 0);

            string movementPath = Dictionary.dropdownState[ActiveSlot == 1 ? "Slot 1 Movement Path" : "Movement Path"];

            switch (movementPath)
            {
                case "Cubic Bezier":
                    Point control1 = new Point(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3);
                    Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, start.Y + 2 * (end.Y - start.Y) / 3);
                    newPosition = MovementPaths.CubicBezier(start, end, control1, control2, 1 - sensitivity);
                    break;
                case "Linear":
                    newPosition = MovementPaths.Lerp(start, end, 1 - sensitivity);
                    break;
                case "Exponential":
                    newPosition = MovementPaths.Exponential(start, end,
                        sensitivity >= 0.99 ? (1 - sensitivity) * 21 : 1 - (sensitivity - 0.2), 3.0);
                    break;
                case "Adaptive":
                    newPosition = MovementPaths.Adaptive(start, end, 1 - sensitivity);
                    break;
                case "Perlin Noise":
                    newPosition = MovementPaths.PerlinNoise(start, end, 1 - sensitivity, 20, 0.5);
                    break;
                default:
                    newPosition = MovementPaths.Lerp(start, end, 1 - sensitivity);
                    break;
            }

            if (frameDeltaSeconds.HasValue)
            {
                double fractionX = targetX == 0 ? 0 : Math.Abs(newPosition.X / (double)targetX);
                double fractionY = targetY == 0 ? 0 : Math.Abs(newPosition.Y / (double)targetY);
                double perFrameFraction = Math.Clamp(Math.Max(fractionX, fractionY), 0.0001, 0.9999);
                double scale = MovementPaths.TimeCorrectedScale(perFrameFraction, frameDeltaSeconds.Value);
                newPosition.X = (int)Math.Round(newPosition.X * scale);
                newPosition.Y = (int)Math.Round(newPosition.Y * scale);
            }

            if (IsEMASmoothingEnabled)
            {
                newPosition.X = (int)EmaSmoothing(previousX, newPosition.X, smoothingFactor);
                newPosition.Y = (int)EmaSmoothing(previousY, newPosition.Y, smoothingFactor);
            }

            newPosition.X = Math.Clamp(newPosition.X, -150, 150);
            newPosition.Y = Math.Clamp(newPosition.Y, -150, 150);
            newPosition.Y = (int)(newPosition.Y / aspectRatioCorrection);
            newPosition.X += jitterX;
            newPosition.Y += jitterY;
            string movementMethod = Dictionary.dropdownState[ActiveSlot == 1 ? "Slot 1 Mouse Movement Method" : "Mouse Movement Method"];

            switch (movementMethod)
            {
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, newPosition.X, newPosition.Y);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, newPosition.X, newPosition.Y, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(newPosition.X, newPosition.Y, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.Move(newPosition.X, newPosition.Y);
                    break;

                default:
                    mouse_event(MOUSEEVENTF_MOVE, (uint)newPosition.X, (uint)newPosition.Y, 0, 0);
                    break;
            }

            previousX = newPosition.X;
            previousY = newPosition.Y;

            bool shouldResetSpray = !Dictionary.toggleState["Auto Trigger"] && !InputBindingManager.IsHoldingBinding("Auto Click Keybind");
            if (shouldResetSpray)
            {
                ResetSprayState();
            }
        }
    }
}

