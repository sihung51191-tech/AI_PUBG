using Aimmy2.Class;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aimmy2.AILogic.Weapons;
using Newtonsoft.Json;

namespace InputLogic
{
    public static class RecoilManager
    {
        public const int TapShotCount = 5;
        private static Thread? recoilThread;
        private static bool isRunning = false;
        public static int SelectedScopeIndex = -1; // -1 = no scope selected
        public static float TemporaryStrengthOffset = 0f;
        private static readonly WeaponScopeProfileStore profileStore = new();
        private static readonly object profileContextLock = new();
        private static string activeWeaponName = "Unknown";
        private static string activeScopeName = "Unknown";
        private static volatile bool autoFireButtonDown;
        public static string ActiveProfileName { get; private set; } = "Legacy scope profile";

        public static void SetRecognitionContext(string weaponName, string scopeName)
        {
            weaponName = NormalizeRecognitionName(weaponName, "Unknown");
            scopeName = NormalizeRecognitionName(scopeName, "chamdo");
            lock (profileContextLock)
            {
                if (string.Equals(activeWeaponName, weaponName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(activeScopeName, scopeName, StringComparison.OrdinalIgnoreCase)) return;
                activeWeaponName = weaponName; activeScopeName = scopeName;
                var profile = profileStore.Resolve(weaponName, scopeName);
                ActiveProfileName = profile == null ? "Legacy scope profile" : $"{profile.WeaponName} + {profile.ScopeName}";
                recoilContext = null;
                ResetTemporaryStrength();
            }
        }

        private static string NormalizeRecognitionName(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? fallback
                : value;
        }

        private static WeaponScopeRecoilProfile? ActiveTypedProfile()
        {
            lock (profileContextLock) return profileStore.Resolve(activeWeaponName, activeScopeName);
        }
        internal static bool HasRecognizedWeapon()
        {
            lock (profileContextLock)
                return !string.IsNullOrWhiteSpace(activeWeaponName)
                    && !activeWeaponName.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                    && !activeWeaponName.Equals("None", StringComparison.OrdinalIgnoreCase)
                    && !activeWeaponName.Equals("Default Weapon", StringComparison.OrdinalIgnoreCase);
        }
        public static IReadOnlyList<WeaponScopeRecoilProfile> GetProfiles() => profileStore.Snapshot();
        public static WeaponScopeRecoilProfile GetProfileForEditing(string weapon, string scope)
        {
            var candidates = profileStore.Snapshot().Where(x => string.Equals(x.WeaponName, weapon, StringComparison.OrdinalIgnoreCase));
            var found = candidates.FirstOrDefault(x => string.Equals(x.ScopeName, scope, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(x => WeaponScopeProfileStore.ScopeNamesEqual(x.ScopeName, scope));
            return found == null ? new WeaponScopeRecoilProfile { WeaponName = weapon, ScopeName = scope }
                : JsonConvert.DeserializeObject<WeaponScopeRecoilProfile>(JsonConvert.SerializeObject(found))!;
        }
        public static void SaveProfile(WeaponScopeRecoilProfile profile)
        {
            profileStore.Upsert(profile);
            RefreshActiveProfile();
        }
        public static void ApplyProfileLive(WeaponScopeRecoilProfile profile)
        {
            profileStore.Upsert(profile, persist: false);
            RefreshActiveProfile();
        }
        private static void RefreshActiveProfile()
        {
            lock (profileContextLock)
            {
                var active = profileStore.Resolve(activeWeaponName, activeScopeName);
                ActiveProfileName = active == null ? "Legacy scope profile" : $"{active.WeaponName} + {active.ScopeName}";
                recoilContext = null; ResetTemporaryStrength();
            }
        }
        public static void RenameProfileLabel(bool scope, string oldLabel, string newLabel) => profileStore.RenameLabel(scope, oldLabel, newLabel);
        private static readonly string[][] defaultScopeLabels =
        {
            new[] { "chamdo", "morong" }, new[] { "2x" }, new[] { "3x" },
            new[] { "4x" }, new[] { "6x" }, new[] { "8x" }
        };
        public static double GetDefaultScopeSensitivity(int scopeIndex)
        {
            int index = Math.Clamp(scopeIndex, 0, defaultScopeLabels.Length - 1);
            return profileStore.GetDefaultScopeSensitivity(defaultScopeLabels[index]);
        }
        public static WeaponScopeRecoilProfile GetDefaultScopeProfileForEditing(string scopeLabel)
        {
            int index = Array.FindIndex(defaultScopeLabels, labels => labels.Any(label => string.Equals(label, scopeLabel, StringComparison.OrdinalIgnoreCase)));
            if (index < 0) index = 0;
            return profileStore.GetOrCreateDefaultScopeForEditing(index + 1, scopeLabel);
        }
        public static void SaveDefaultScopeProfile(WeaponScopeRecoilProfile profile)
        {
            profile.WeaponName = "Default Weapon";
            profile.Enabled = true;
            profile.SensitivityMultiplier = 1;
            string scope = NormalizeRecognitionName(profile.ScopeName, "chamdo");
            int index = Array.FindIndex(defaultScopeLabels, labels => labels.Any(label => string.Equals(label, scope, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
            {
                profile.ScopeName = scope;
                SaveProfile(profile);
                return;
            }

            foreach (string alias in defaultScopeLabels[index])
            {
                var copy = JsonConvert.DeserializeObject<WeaponScopeRecoilProfile>(JsonConvert.SerializeObject(profile))!;
                copy.WeaponName = "Default Weapon";
                copy.ScopeName = alias;
                copy.Enabled = true;
                copy.SensitivityMultiplier = 1;
                profileStore.Upsert(copy);
            }
            RefreshActiveProfile();
        }
        public static void SetDefaultScopeSensitivity(int scopeIndex, double sensitivity)
        {
            int index = Math.Clamp(scopeIndex, 0, defaultScopeLabels.Length - 1);
            profileStore.SetDefaultScopeSensitivity(index + 1, defaultScopeLabels[index], Math.Clamp(sensitivity, 0, 2));
            lock (profileContextLock)
            {
                var active = profileStore.Resolve(activeWeaponName, activeScopeName);
                ActiveProfileName = active == null ? "Legacy scope profile" : $"{active.WeaponName} + {active.ScopeName}";
                recoilContext = null;
                ResetTemporaryStrength();
            }
        }

        private static (int Scope, int Slot, bool Tap, bool Enabled, bool Wheel)? recoilContext;

        internal static bool SynchronizeContext(int scope, int slot, bool tap, bool enabled, bool wheel)
        {
            var next = (scope, slot, tap, enabled, wheel);
            bool changed = recoilContext != next;
            recoilContext = next;
            if (changed || ((!enabled || !wheel) && TemporaryStrengthOffset != 0))
                ResetTemporaryStrength();
            return changed;
        }

        internal static float GetContinuousForce(int scopeNum, double elapsedSeconds)
        {
            var profile = ActiveTypedProfile();
            if (profile != null && profile.ContinuousStages.Count > 0)
            {
                double typedBoundary = 0; int index = profile.ContinuousStages.Count - 1;
                for (int i = 0; i < profile.ContinuousStages.Count - 1; i++)
                { typedBoundary += Math.Max(0, profile.ContinuousStages[i].DurationSeconds); if (elapsedSeconds < typedBoundary) { index = i; break; } }
                float typedForce = (float)(profile.ContinuousStages[index].Force * profile.SensitivityMultiplier);
                bool typedWheel = Dictionary.toggleState.TryGetValue("Mouse Wheel Adjust", out var typedValue) && (bool)typedValue;
                return typedForce > 0 ? Math.Max(0, typedForce + (typedWheel ? TemporaryStrengthOffset : 0)) : 0;
            }
            double boundary = 0;
            int stage = 4;
            for (int i = 1; i < 4; i++)
            {
                boundary += Math.Max(0, GetSetting($"Recoil Scope {scopeNum} S{i} Time", 0));
                if (elapsedSeconds < boundary) { stage = i; break; }
            }
            float force = GetSetting($"Recoil Scope {scopeNum} S{stage} Force", 0);
            bool wheel = Dictionary.toggleState.TryGetValue("Mouse Wheel Adjust", out var value) && (bool)value;
            return force > 0 ? Math.Max(0, force + (wheel ? TemporaryStrengthOffset : 0)) : 0;
        }

        
        // Gentler continuous recoil. Tap impulses keep their existing distance.
        internal static int AccumulateContinuousPull(ref float accumulator, float force)
        {
            if (!float.IsFinite(force) || force <= 0) return 0;
            accumulator += force * 0.5f;
            int pixels = (int)accumulator;
            accumulator -= pixels;
            return pixels;
        }

        public static void SetStageForce(int scope, string key, double value)
        {
            if (scope < 1 || scope > 6 || !key.StartsWith($"Recoil Scope {scope} S") || !key.EndsWith(" Force"))
                throw new ArgumentException("Invalid recoil force setting");
            Dictionary.sliderSettings[key] = value;
            if (SelectedScopeIndex == scope - 1) ResetTemporaryStrength();
        }

        public class RecoilSettings
        {
            public float Strength;
            public float Step;
            public float Delay;
            public float Multi;
        }
        public static RecoilSettings? ActiveSlotSettings;
        
        // Windows API
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int INPUT_MOUSE = 0;
        private const int MOUSEEVENTF_MOVE = 0x0001;
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CURSORINFO pci);

        private const int CURSOR_SHOWING = 0x00000001;

        public static void Initialize()
        {
            if (isRunning) return;
            isRunning = true;
            recoilThread = new Thread(RecoilLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            recoilThread.Start();
            
            // Start Mouse Hook
            GlobalMouseHook.OnMouseWheel += HandleMouseScroll;
            GlobalMouseHook.Start();
        }

        private static bool HandleMouseScroll(int delta)
        {
            // Only adjust if Recoil Control is enabled and a scope is selected
            if (!Dictionary.toggleState.ContainsKey("Scope Recoil Control") || !Dictionary.toggleState["Scope Recoil Control"]) return false;
            
            // Check if Mouse Wheel Adjust is enabled
            if (!Dictionary.toggleState.ContainsKey("Mouse Wheel Adjust") || !Dictionary.toggleState["Mouse Wheel Adjust"]) return false;

            // If "Enable Model Switch Keybind" is on, it might interfere if using scroll there too, but usually fine.
            if (SelectedScopeIndex < 0) return false;

            // Check if cursor is visible (e.g. in menu, alt-tabbed)
            // If visible, DO NOT adjust recoil
            CURSORINFO pci;
            pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            if (GetCursorInfo(out pci))
            {
                if ((pci.flags & CURSOR_SHOWING) == CURSOR_SHOWING)
                {
                    return false;
                }
            }

            // Adjust by the configured step size or default to 2.0f if not present
            double stepVal = 2.0;
            if (Dictionary.sliderSettings.TryGetValue("Mouse Wheel Adjust Step", out var stepObj))
            {
                try { stepVal = Convert.ToDouble(stepObj); } catch { }
            }
            float adjustment = (delta > 0) ? (float)stepVal : -(float)stepVal;
            
            TemporaryStrengthOffset += adjustment;
            
            // Update UI
            if (Dictionary.DetectedScopeOverlay != null)
            {
                Dictionary.DetectedScopeOverlay.UpdateRecoilAdj(TemporaryStrengthOffset.ToString("+0.0;-0.0;0"));
            }
            
            return true; // Block the event
        }

        public static void ResetTemporaryStrength()
        {
            TemporaryStrengthOffset = 0;
            if (Dictionary.DetectedScopeOverlay != null)
            {
                Dictionary.DetectedScopeOverlay.UpdateRecoilAdj("0");
            }
        }

        public static void Stop()
        {
            isRunning = false;
            recoilThread?.Join(500);
            ReleaseAutoFireButton();
            GlobalMouseHook.Stop();
        }

        private static void RecoilLoop()
        {
            bool[] keyWasPressed = new bool[7];
            bool mouseButtonsHeld = false;
            float pixelAccumulator = 0; // Accumulate fractional movements
            DateTime? dragStartTime = null;
            bool leftWasPressed = false;
            int tapShot = 0;
            long lastTapActivity = -1;
            int tapScope = -1, tapSlot = -1;
            long lastError = -10000;
            bool autoFireConsumedForAim = false;
            long autoFireReleaseAt = -1;
            long lastAutoFireAt = -1;
            AutoFireMode previousAutoFireMode = AutoFireMode.None;
            string previousAutoFireProfile = "";

            while (isRunning)
            {
                try
                {
                    bool leftButtonPressed = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                    bool rightButtonPressed = (GetAsyncKeyState(0x02) & 0x8000) != 0;
                    bool aimButtonPressed = InputBindingManager.IsHoldingBinding("Aim Keybind")
                        || InputBindingManager.IsHoldingBinding("Second Aim Keybind");
                    bool recoilAimPressed = aimButtonPressed || rightButtonPressed;
                    bool newShot = leftButtonPressed && !leftWasPressed;
                    leftWasPressed = leftButtonPressed;
                    if (Dictionary.toggleState.ContainsKey("Scope Recoil Control") && Dictionary.toggleState["Scope Recoil Control"])
                    {
                        // Check keys for scope selection (1-6) - skip if Weapon Recognition is handling slots
                        if (!Dictionary.toggleState.ContainsKey("Weapon Recognition") || !Dictionary.toggleState["Weapon Recognition"])
                        {
                            for (int i = 0; i < 6; i++)
                            {
                                int scopeNum = i + 1;
                                string keySetting = $"Recoil Scope {scopeNum} Keybind";

                                if (Dictionary.bindingSettings.ContainsKey(keySetting))
                                {
                                    string binding = Dictionary.bindingSettings[keySetting];
                                    if (string.IsNullOrEmpty(binding) || binding == "None") continue;

                                    bool isPressed = IsBindingPressed(binding);

                                    if (isPressed && !keyWasPressed[i])
                                    {
                                        SelectedScopeIndex = i;
                                        pixelAccumulator = 0;
                                    }
                                    keyWasPressed[i] = isPressed;
                                }
                            }
                        }

                        int selectedScope = SelectedScopeIndex;
                        int currentSlot = Aimmy2.AILogic.AIManager.ActiveSlot;
                        var activeProfile = ActiveTypedProfile();
                        long now = Environment.TickCount64;
                        AutoFireMode autoMode = activeProfile?.AutoFire?.Mode ?? AutoFireMode.None;
                        string autoProfileKey = activeProfile == null ? "" : $"{activeProfile.WeaponName}\n{activeProfile.ScopeName}";
                        if (autoMode != previousAutoFireMode || !string.Equals(autoProfileKey, previousAutoFireProfile, StringComparison.OrdinalIgnoreCase))
                        {
                            ReleaseAutoFireButton();
                            autoFireConsumedForAim = false;
                            autoFireReleaseAt = -1;
                            lastAutoFireAt = -1;
                            previousAutoFireMode = autoMode;
                            previousAutoFireProfile = autoProfileKey;
                        }
                        if (autoFireButtonDown && autoMode != AutoFireMode.Ar && now >= autoFireReleaseAt)
                            ReleaseAutoFireButton();

                        bool autoFireActive = aimButtonPressed && activeProfile?.Enabled == true && HasRecognizedWeapon()
                            && Aimmy2.AILogic.AIManager.HasRecentAimTarget() && autoMode != AutoFireMode.None;
                        if (!aimButtonPressed)
                        {
                            ReleaseAutoFireButton();
                            autoFireConsumedForAim = false;
                            autoFireReleaseAt = -1;
                            lastAutoFireAt = -1;
                        }
                        else if (autoFireActive)
                        {
                            switch (autoMode)
                            {
                                case AutoFireMode.Sr when !autoFireConsumedForAim:
                                    PressAutoFireButton();
                                    autoFireReleaseAt = now + 18;
                                    autoFireConsumedForAim = true;
                                    break;
                                case AutoFireMode.Ar:
                                    PressAutoFireButton();
                                    break;
                                case AutoFireMode.Dmr:
                                case AutoFireMode.Shotgun:
                                    double interval = autoMode == AutoFireMode.Dmr
                                        ? activeProfile!.AutoFire.DmrIntervalMs
                                        : activeProfile!.AutoFire.ShotgunIntervalMs;
                                    interval = Math.Clamp(interval, 25, 2000);
                                    if (!autoFireButtonDown && (lastAutoFireAt < 0 || now - lastAutoFireAt >= interval))
                                    {
                                        PressAutoFireButton();
                                        autoFireReleaseAt = now + Math.Min(18, (long)interval / 2);
                                        lastAutoFireAt = now;
                                    }
                                    break;
                            }
                        }
                        else ReleaseAutoFireButton();
                        bool tapMode = activeProfile?.Tap.Enabled ??
                            (Dictionary.toggleState.TryGetValue($"Recoil Scope {selectedScope + 1} Tap", out var tap) && (bool)tap);
                        bool wheel = Dictionary.toggleState.TryGetValue("Mouse Wheel Adjust", out var wheelValue) && (bool)wheelValue;
                        if (SynchronizeContext(selectedScope, currentSlot, tapMode, true, wheel))
                        {
                            mouseButtonsHeld = false;
                            dragStartTime = null;
                            pixelAccumulator = 0;
                        }
                        if (!recoilAimPressed || !tapMode || selectedScope != tapScope || currentSlot != tapSlot)
                        {
                            tapShot = 0;
                            lastTapActivity = -1;
                        }
                        tapScope = selectedScope;
                        tapSlot = currentSlot;

                        if (leftButtonPressed && recoilAimPressed && selectedScope >= 0)
                        {
                            if (!mouseButtonsHeld)
                            {
                                dragStartTime = DateTime.Now;
                                pixelAccumulator = 0; // Reset on new click
                            }

                            int scopeNum = selectedScope + 1;
                            if (tapMode)
                            {
                                // One impulse per left-button press while aiming. Holding,
                                // changing scope, or enabling tap cannot repeat the impulse.
                                if (newShot)
                                {
                                    double resetSeconds = activeProfile == null
                                        ? GetBoundedTapSetting($"Recoil Scope {scopeNum} Tap Reset Time", 1, 0.1f, 10)
                                        : Math.Clamp(activeProfile.Tap.ResetSeconds, .1, 10);
                                    double resetMs = resetSeconds * 1000;
                                    if (lastTapActivity < 0 || Environment.TickCount64 - lastTapActivity >= resetMs)
                                        tapShot = 0;
                                    tapShot = Math.Min(tapShot + 1, TapShotCount);
                                    int pull = (int)Math.Round(GetTapShotDistance(scopeNum, tapShot));
                                    if (pull > 0) MoveMouseDown(pull);
                                }
                                // Measure the pause after releasing fire, not time spent holding it.
                                lastTapActivity = Environment.TickCount64;
                                pixelAccumulator = 0;
                                mouseButtonsHeld = true;
                                Thread.Sleep(5);
                                continue;
                            }
                            double elapsedSeconds = (DateTime.Now - dragStartTime.GetValueOrDefault()).TotalSeconds;
                            float currentForce = GetContinuousForce(scopeNum, elapsedSeconds);

                            // Apply movement
                            if (currentForce > 0)
                            {
                                int pixelsToMove = AccumulateContinuousPull(ref pixelAccumulator, currentForce);

                                if (pixelsToMove >= 1)
                                {
                                    MoveMouseDown(pixelsToMove);
                                }
                            }

                            mouseButtonsHeld = true;
                        }
                        else
                        {
                            if (mouseButtonsHeld)
                            {
                                dragStartTime = null;
                                pixelAccumulator = 0;
                            }
                            mouseButtonsHeld = false;
                        }
                    }
                    else
                    {
                        ReleaseAutoFireButton();
                        SynchronizeContext(SelectedScopeIndex, Aimmy2.AILogic.AIManager.ActiveSlot, false, false, false);
                        tapShot = 0;
                        lastTapActivity = -1;
                        mouseButtonsHeld = false;
                        dragStartTime = null;
                        pixelAccumulator = 0;
                    }
                }
                catch (Exception ex)
                {
                    if (Environment.TickCount64 - lastError >= 10000)
                    {
                        lastError = Environment.TickCount64;
                        Other.LogManager.Log(Other.LogManager.LogLevel.Error, $"Continuous/tap recoil loop: {ex}");
                    }
                }

                Thread.Sleep(5); // Run loop ~200Hz
            }
            ReleaseAutoFireButton();
        }

        public static float GetTapShotDistance(int scopeNum, int shot)
        {
            var profile = ActiveTypedProfile();
            if (profile?.Tap.ShotDistances.Count > 0)
                return (float)Math.Max(0, profile.Tap.ShotDistances[Math.Clamp(shot, 1, profile.Tap.ShotDistances.Count) - 1] * profile.SensitivityMultiplier);
            float legacy = GetBoundedTapSetting($"Recoil Scope {scopeNum} Tap Distance", 40, 0, 2000);
            float value = GetSetting($"Recoil Scope {scopeNum} Tap Shot {Math.Clamp(shot, 1, TapShotCount)}", -1);
            return float.IsFinite(value) && value >= 0 ? Math.Clamp(value, 0, 2000) : legacy;
        }

        private static float GetBoundedTapSetting(string key, float fallback, float minimum, float maximum)
        {
            float value = GetSetting(key, fallback);
            return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
        }

        private static float GetSetting(string key, float defaultValue)
        {
            if (Dictionary.sliderSettings.ContainsKey(key))
            {
                return (float)Dictionary.sliderSettings[key];
            }
            return defaultValue;
        }

        private static bool IsBindingPressed(string binding)
        {
            if (string.IsNullOrEmpty(binding) || binding == "None") return false;

            string[] parts = binding.Split('+');
            foreach (string part in parts)
            {
                int vk = 0;
                if (part == "Control") vk = 0x11;
                else if (part == "Alt") vk = 0x12;
                else if (part == "Shift") vk = 0x10;
                else vk = GetVirtualKeyCode(part);

                if (vk == 0 || (GetAsyncKeyState(vk) & 0x8000) == 0) return false;
            }
            return true;
        }

        private static int GetVirtualKeyCode(string keyName)
        {
            if (string.IsNullOrEmpty(keyName) || keyName == "None") return 0;

            // Handle Common Mouse Buttons
            switch (keyName)
            {
                case "Left": return 0x01;
                case "Right": return 0x02;
                case "Middle": return 0x04;
                case "XButton1": return 0x05;
                case "XButton2": return 0x06;
            }

            // Standard Keys
            if (Enum.TryParse(keyName, out System.Windows.Forms.Keys key))
            {
                return (int)key;
            }

            return 0;
        }

        private static void MoveMouseDown(int pixels)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dx = 0;
            inputs[0].mi.dy = pixels;
            inputs[0].mi.mouseData = 0;
            inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;
            inputs[0].mi.time = 0;
            inputs[0].mi.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void PressAutoFireButton()
        {
            if (autoFireButtonDown) return;
            if (SendMouseButton(MOUSEEVENTF_LEFTDOWN)) autoFireButtonDown = true;
        }

        private static void ReleaseAutoFireButton()
        {
            if (!autoFireButtonDown) return;
            SendMouseButton(MOUSEEVENTF_LEFTUP);
            autoFireButtonDown = false;
        }

        private static bool SendMouseButton(uint flags)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = flags;
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }
}
