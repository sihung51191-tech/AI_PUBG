using Gma.System.MouseKeyHook;
using System.Windows.Forms;

namespace InputLogic
{
    internal class InputBindingManager
    {
        private IKeyboardMouseEvents? _mEvents;
        private readonly Dictionary<string, string> bindings = [];
        private static readonly Dictionary<string, bool> isHolding = [];
        private string? settingBindingId = null;

        public event Action<string, string>? OnBindingSet;
        public event Action<string>? OnBindingPressed;
        public event Action<string>? OnBindingReleased;
        public event Action<Keys>? OnAnyKeyDown;
        public event Action<Keys>? OnAnyKeyUp;

        private static readonly HashSet<string> currentlyPressedKeys = [];
        private static readonly HashSet<Keys> currentlyPressedKeyboardKeys = [];
        private static readonly object keyStateGate = new();

        public static bool IsHoldingBinding(string bindingId) => isHolding.TryGetValue(bindingId, out bool holding) && holding;

        public void SetupDefault(string bindingId, string keyCode)
        {
            bindings[bindingId] = keyCode;
            isHolding[bindingId] = false;
            OnBindingSet?.Invoke(bindingId, keyCode);
            EnsureHookEvents();
        }

        public string GetBinding(string bindingId) => bindings.GetValueOrDefault(bindingId, "None");

        public bool IsBindingExactlyHeld(string bindingId)
        {
            return bindings.TryGetValue(bindingId, out string? combo) && IsComboHeld(combo, exactModifiers: true);
        }

        public bool IsScanBindingHeld(string bindingId)
        {
            if (!bindings.TryGetValue(bindingId, out string? combo) || !IsComboHeld(combo)) return false;
            bool bindingUsesAlt = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("Alt", StringComparer.OrdinalIgnoreCase);
            return bindingUsesAlt || (Control.ModifierKeys & Keys.Alt) != Keys.Alt;
        }

        public bool IsKeyPartOfBinding(string bindingId, Keys key)
        {
            return bindings.TryGetValue(bindingId, out string? combo) && BindingIncludesKey(combo, key);
        }

        public IReadOnlyCollection<Keys> GetPressedKeyboardKeys()
        {
            lock (keyStateGate) return currentlyPressedKeyboardKeys.ToArray();
        }

        public void ResetTransientInputState()
        {
            lock (keyStateGate)
            {
                currentlyPressedKeys.Clear();
                currentlyPressedKeyboardKeys.Clear();
            }
            foreach (string id in isHolding.Keys.ToArray()) isHolding[id] = false;
        }

        internal static bool BindingIncludesKey(string combo, Keys key)
        {
            if (string.IsNullOrWhiteSpace(combo) || combo == "None") return false;
            string keyName = key switch
            {
                Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Control",
                Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
                _ => key.ToString()
            };
            return combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(keyName, StringComparer.OrdinalIgnoreCase);
        }

        internal static bool ModifiersMatchExactly(string combo, Keys modifiers)
        {
            var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool wantsControl = parts.Contains("Control", StringComparer.OrdinalIgnoreCase);
            bool wantsAlt = parts.Contains("Alt", StringComparer.OrdinalIgnoreCase);
            bool wantsShift = parts.Contains("Shift", StringComparer.OrdinalIgnoreCase);
            bool hasControl = (modifiers & Keys.Control) == Keys.Control;
            bool hasAlt = (modifiers & Keys.Alt) == Keys.Alt;
            bool hasShift = (modifiers & Keys.Shift) == Keys.Shift;
            return wantsControl == hasControl && wantsAlt == hasAlt && wantsShift == hasShift;
        }

        public void StartListeningForBinding(string bindingId)
        {
            settingBindingId = bindingId;
            EnsureHookEvents();
        }

        private void EnsureHookEvents()
        {
            if (_mEvents == null)
            {
                _mEvents = Hook.GlobalEvents();
                _mEvents.KeyDown += GlobalHookKeyDown!;
                _mEvents.MouseDown += GlobalHookMouseDown!;
                _mEvents.KeyUp += GlobalHookKeyUp!;
                _mEvents.MouseUp += GlobalHookMouseUp!;
            }
        }

        private bool IsModifier(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }

        private void GlobalHookKeyDown(object sender, KeyEventArgs e)
        {
            lock (keyStateGate)
            {
                currentlyPressedKeys.Add(e.KeyCode.ToString());
                currentlyPressedKeyboardKeys.Add(e.KeyCode);
            }
            if (settingBindingId == null) OnAnyKeyDown?.Invoke(e.KeyCode);

            if (settingBindingId != null)
            {
                string combo = "";
                string keyPart = e.KeyCode.ToString();
                
                // If it's a modifier key, we don't want to prefix it with itself (e.g. no "Shift+LShiftKey")
                bool isControl = (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey);
                bool isAlt = (e.KeyCode == Keys.Menu || e.KeyCode == Keys.LMenu || e.KeyCode == Keys.RMenu);
                bool isShift = (e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey);

                if (e.Control && !isControl) combo += "Control+";
                if (e.Alt && !isAlt) combo += "Alt+";
                if (e.Shift && !isShift) combo += "Shift+";
                
                combo += keyPart;

                string id = settingBindingId;
                bindings[id] = combo;
                OnBindingSet?.Invoke(id, combo);
                settingBindingId = null;
                return;
            }
            
            CheckAndTriggerBindings();
        }

        private void GlobalHookMouseDown(object sender, MouseEventArgs e)
        {
            lock (keyStateGate) currentlyPressedKeys.Add(e.Button.ToString());

            if (settingBindingId != null)
            {
                string combo = "";
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) combo += "Control+";
                if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt) combo += "Alt+";
                if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) combo += "Shift+";
                combo += e.Button.ToString();

                string id = settingBindingId;
                bindings[id] = combo;
                OnBindingSet?.Invoke(id, combo);
                settingBindingId = null;
                return;
            }

            CheckAndTriggerBindings();
        }

        private void GlobalHookKeyUp(object sender, KeyEventArgs e)
        {
            lock (keyStateGate)
            {
                currentlyPressedKeys.Remove(e.KeyCode.ToString());
                currentlyPressedKeyboardKeys.Remove(e.KeyCode);
            }
            if (settingBindingId == null) OnAnyKeyUp?.Invoke(e.KeyCode);
            CheckAndTriggerBindings();
        }

        private void GlobalHookMouseUp(object sender, MouseEventArgs e)
        {
            lock (keyStateGate) currentlyPressedKeys.Remove(e.Button.ToString());
            CheckAndTriggerBindings();
        }

        private void CheckAndTriggerBindings()
        {
            foreach (var binding in bindings)
            {
                bool pressed = IsComboHeld(binding.Value);
                bool wasHeld = isHolding.GetValueOrDefault(binding.Key, false);

                if (pressed && !wasHeld)
                {
                    isHolding[binding.Key] = true;
                    OnBindingPressed?.Invoke(binding.Key);
                }
                else if (!pressed && wasHeld)
                {
                    isHolding[binding.Key] = false;
                    OnBindingReleased?.Invoke(binding.Key);
                }
            }
        }

        private bool IsComboHeld(string combo, bool exactModifiers = false)
        {
            if (string.IsNullOrEmpty(combo) || combo == "None") return false;
            if (exactModifiers && !ModifiersMatchExactly(combo, Control.ModifierKeys)) return false;
            var parts = combo.Split('+');
            foreach (var part in parts)
            {
                if (part == "Control") { if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return false; }
                else if (part == "Alt") { if ((Control.ModifierKeys & Keys.Alt) != Keys.Alt) return false; }
                else if (part == "Shift") { if ((Control.ModifierKeys & Keys.Shift) != Keys.Shift) return false; }
                else { lock (keyStateGate) if (!currentlyPressedKeys.Contains(part)) return false; }
            }
            return true;
        }

        public void StopListening()
        {
            if (_mEvents != null)
            {
                _mEvents.KeyDown -= GlobalHookKeyDown!;
                _mEvents.MouseDown -= GlobalHookMouseDown!;
                _mEvents.KeyUp -= GlobalHookKeyUp!;
                _mEvents.MouseUp -= GlobalHookMouseUp!;
                _mEvents.Dispose();
                _mEvents = null;
            }
            ResetTransientInputState();
        }
    }
}
