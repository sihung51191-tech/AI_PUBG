using Visuality;

namespace Aimmy2.Class
{
    public static class Dictionary
    {
        static Dictionary()
        {
            // Register keys before strict configuration loading. -1 inherits the old
            // per-scope distance until an individual shot is configured.
            for (int scope = 1; scope <= 6; scope++)
            {
                sliderSettings[$"Recoil Scope {scope} Tap Reset Time"] = 1.0;
                double defaultForce = Convert.ToDouble(sliderSettings[$"Recoil Scope {scope} Strength"]);
                for (int stage = 1; stage <= 4; stage++)
                {
                    sliderSettings.TryAdd($"Recoil Scope {scope} S{stage} Force", defaultForce);
                    sliderSettings.TryAdd($"Recoil Scope {scope} S{stage} Time", 0.0);
                }
                sliderSettings[$"Recoil Scope {scope} S4 Time"] = 0.0;
                for (int shot = 1; shot <= InputLogic.RecoilManager.TapShotCount; shot++)
                    sliderSettings[$"Recoil Scope {scope} Tap Shot {shot}"] = -1.0;
            }
        }

        public static readonly System.Threading.SemaphoreSlim ModelLoadSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        public static string lastLoadedModel = "N/A";
        public static string lastLoadedModelSlot2 = "N/A";
        public static string lastLoadedConfig = "N/A";
        public static DetectedPlayerWindow? DetectedPlayerOverlay;
        public static FOV? FOVWindow;
        public static DetectedScopeWindow? DetectedScopeOverlay;
        public static CrosshairWindow? CrosshairWindow;

        public static Dictionary<string, dynamic> bindingSettings = new()
        {
            { "Aim Keybind", "Right"},
            { "Second Aim Keybind", "LMenu"},
            { "Dynamic FOV Keybind", "Left"},
            { "Emergency Stop Keybind", "Delete"},
            { "Model Switch Keybind", "OemPipe"},
            { "Recoil Toggle Keybind", "None" },
            { "Recoil Scope 1 Keybind", "D1" },
            { "Recoil Scope 2 Keybind", "D2" },
            { "Recoil Scope 3 Keybind", "D3" },
            { "Recoil Scope 4 Keybind", "D4" },
            { "Recoil Scope 5 Keybind", "D5" },
            { "Recoil Scope 6 Keybind", "D6" },
            { "Weapon Scan Keybind", "Tab" },
            { "Scope Scan Keybind", "None" },
            { "Weapon Slot 1 Keybind", "D1" },
            { "Weapon Slot 2 Keybind", "D2" },
            { "Weapon Cancel Keybind", "Escape" },
            { "Auto Click Keybind", "None" },
            { "Slot 1 Priority Key", "None" },
            { "Slot 2 Priority Key", "None" },
            { "Crosshair Hide Key 1", "Right" },
            { "Fast Loot Keybind", "MButton" }
        };

        public static Dictionary<string, dynamic> sliderSettings = new()
        {
            { "Recoil Scope 1 Tap Distance", 40.0 },
            { "Recoil Scope 2 Tap Distance", 40.0 },
            { "Recoil Scope 3 Tap Distance", 40.0 },
            { "Recoil Scope 4 Tap Distance", 40.0 },
            { "Recoil Scope 5 Tap Distance", 40.0 },
            { "Recoil Scope 6 Tap Distance", 40.0 },
            { "Suggested Model", ""},
            { "FOV Size", 640 },
            { "Capture Size", 0 },
            { "AI FPS Limit", 0 },
            { "Crosshair Size", 6 },
            { "Dynamic FOV Size", 200 },
            { "Mouse Sensitivity (+/-)", 0.80 },
            { "Slot 1 Mouse Sensitivity", 0.80 },
            { "Mouse Jitter", 4 },
            { "Slot 1 Mouse Jitter", 4 },
            { "Sticky Aim Threshold", 50 },
            { "Target Lock Duration", 500 },
            { "Y Offset (Up/Down)", 0 },
            { "Slot 1 Y Offset (Up/Down)", 0 },
            { "Y Offset (%)", 50 },
            { "Slot 1 Y Offset (%)", 50 },
            { "X Offset (Left/Right)", 0 },
            { "Slot 1 X Offset (Left/Right)", 0 },
            { "X Offset (%)", 50 },
            { "Slot 1 X Offset (%)", 50 },
            { "EMA Smoothening", 0.5},
            { "Kalman Lead Time", 0.10 },
            { "Prediction Time", 35.0 },
            { "Kalman Smoothness", 55.0 },
            { "Maximum Missing Frames", 3.0 },
            { "Maximum Frame Age", 150.0 },
            { "Sticky Maximum Missing Frames", 3.0 },
            { "Sticky Maximum Frame Age", 150.0 },
            { "Maximum Prediction Distance", 0.0 },
            { "WiseTheFox Lead Time", 0.15 },
            { "Shalloe Lead Multiplier", 3.0 },
            { "CA Lead Multiplier", 0.10 },
            { "Auto Trigger Delay", 0.1 },
            { "AI Minimum Confidence", 45 },
            { "Slot 2 AI Minimum Confidence", 45 },
            { "AI Confidence Font Size", 20 },
            { "Corner Radius", 0 },
            { "Border Thickness", 1 },
            { "Opacity", 1 },
            { "Weapon + Scope Info Size", 82.0 },
            { "Weapon + Scope Info Opacity", 90.0 },
            // Recoil
            // Recoil
            { "Recoil Scope 1 Strength", 70.0 },
            { "Recoil Scope 1 Step", 1.0 },
            { "Recoil Scope 1 Delay", 2.0 },
            { "Recoil Scope 1 Multi", 5.0 },
            
            { "Recoil Scope 2 Strength", 99.0 },
            { "Recoil Scope 2 Step", 1.0 },
            { "Recoil Scope 2 Delay", 2.0 },
            { "Recoil Scope 2 Multi", 5.0 },
            
            { "Recoil Scope 3 Strength", 200.0 },
            { "Recoil Scope 3 Step", 1.0 },
            { "Recoil Scope 3 Delay", 2.0 },
            { "Recoil Scope 3 Multi", 5.0 },
            
            { "Recoil Scope 4 Strength", 200.0 },
            { "Recoil Scope 4 Step", 1.0 },
            { "Recoil Scope 4 Delay", 2.0 },
            { "Recoil Scope 4 Multi", 5.0 },
            
            { "Recoil Scope 5 Strength", 151.0 },
            { "Recoil Scope 5 Step", 1.0 },
            { "Recoil Scope 5 Delay", 2.0 },
            { "Recoil Scope 5 Multi", 5.0 },
            
            { "Recoil Scope 6 Strength", 200.0 },
            { "Recoil Scope 6 Step", 1.0 },
            { "Recoil Scope 6 Delay", 2.0 },
            { "Recoil Scope 6 Multi", 5.0 },
            
            // Weapon Regions (Default 1920x1080)
            { "Weapon 1 X", 1604 },
            { "Weapon 1 Y", 113 },
            { "Weapon 1 Width", 53 },
            { "Weapon 1 Height", 53 },
            
            { "Weapon 2 X", 1603 },
            { "Weapon 2 Y", 337 },
            { "Weapon 2 Width", 56 },
            { "Weapon 2 Height", 55 },
            
            // Fast Loot Coordinates
            { "Loot Item 1 X", 229.0 },
            { "Loot Item 1 Y", 455.0 },
            { "Loot Item 2 X", 190.0 },
            { "Loot Item 2 Y", 395.0 },
            { "Loot Item 3 X", 196.0 },
            { "Loot Item 3 Y", 330.0 },
            { "Loot Item 4 X", 196.0 },
            { "Loot Item 4 Y", 270.0 },
            { "Loot Item 5 X", 192.0 },
            { "Loot Item 5 Y", 208.0 },
            { "Loot Item 6 X", 191.0 },
            { "Loot Item 6 Y", 142.0 },
            { "Loot Inventory X", 894.0 },
            { "Loot Inventory Y", 206.0 },
            { "Fast Loot Delay", 5.0 }
            ,{ "Weapon Template Confidence Threshold", 85.0 }
            ,{ "Weapon Template Scale Min", 0.70 }
            ,{ "Weapon Template Scale Max", 1.30 }
            ,{ "Weapon Template Scale Step", 0.05 }
            ,{ "Scope Template Confidence Threshold", 85.0 }
            ,{ "Scope Template Scale Min", 0.70 }
            ,{ "Scope Template Scale Max", 1.30 }
            ,{ "Scope Template Scale Step", 0.05 }
            ,{ "ORB Feature Count", 500.0 }
            ,{ "ORB Ratio Threshold", 0.75 }
            ,{ "ORB Minimum Good Matches", 8.0 }
            ,{ "ORB RANSAC Threshold", 3.0 }
            ,{ "SIFT Feature Count", 500.0 }
            ,{ "SIFT Ratio Threshold", 0.72 }
            ,{ "SIFT Minimum Good Matches", 8.0 }
            ,{ "SIFT RANSAC Threshold", 3.0 }
            ,{ "SIFT Scan Interval", 300.0 }
            ,{ "Weapon AI Confidence", 45.0 }
            ,{ "Default Scope Sensitivity", 100.0 }
        };

        // Make sure the Settings Name is the EXACT Same as the Toggle Name or I will smack you :joeangy:
        // nori
        public static Dictionary<string, dynamic> toggleState = new()
        {
            { "Recoil Scope 1 Tap", false },
            { "Recoil Scope 2 Tap", false },
            { "Recoil Scope 3 Tap", false },
            { "Recoil Scope 4 Tap", false },
            { "Recoil Scope 5 Tap", false },
            { "Recoil Scope 6 Tap", false },
            { "Aim Assist", false },
            { "Sticky Aim", true },
            { "Enable Kalman Filter", true },
            { "Constant AI Tracking", false },
            { "Predictions", false },
            { "EMA Smoothening", false },
            { "Enable Model Switch Keybind", true },
            { "Auto Trigger", false },
            { "FOV", false },
            { "Dynamic FOV", false },
            { "Third Person Support", false },
            { "Masking", false },
            { "Show Detected Player", false },
            { "Show Detection Performance", false },
            { "Cursor Check", false },
            { "Spray Mode", false },
            //{ "Only When Held", false },
            { "Show FOV", true },
            { "Show AI Confidence", false },
            { "Show Tracers", false },
            { "Collect Data While Playing", false },
            { "Auto Label Data", false },
            { "LG HUB Mouse Movement", false },
            { "Mouse Background Effect", true },
            { "Debug Mode", false },
            { "UI TopMost", false },
            //--
            { "StreamGuard", false },
            //--
            { "X Axis Percentage Adjustment", false },
            { "Slot 1 X Axis Percentage Adjustment", false },
            { "Y Axis Percentage Adjustment", false },
            { "Slot 1 Y Axis Percentage Adjustment", false },
            { "Scope Recoil Control", false },
            { "Mouse Wheel Adjust", false },
            { "Weapon Recognition", false },
            { "Scope Recognition", false },
            { "Show Weapon + Scope Info", false },
            { "Slot 1 Priority Aiming", true },
            { "Slot 2 Priority Aiming", true },
            { "Toggle Weapon Scan", false },
            { "Toggle Scope Scan", false },
            { "Virtual Crosshair", false },
            { "Fast Loot", false }
            ,{ "Weapon Template Multi-scale", true }
            ,{ "Weapon Template Edge Matching", true }
            ,{ "Weapon Template Alpha Mask", true }
            ,{ "Weapon Template Color Mask", true }
            ,{ "Scope Template Multi-scale", true }
            ,{ "Scope Template Edge Matching", true }
            ,{ "Scope Template Alpha Mask", true }
            ,{ "Scope Template Color Mask", true }
        };

        public static Dictionary<string, dynamic> minimizeState = new()
        {
            { "Aim Assist", false },
            { "Aim Config", false },
            { "Aim Config (Slot 1)", false },
            { "Aim Config (Slot 2)", false },
            { "Predictions", false },
            { "Auto Trigger", false },
            { "FOV Config", false },
            { "ESP Config", false },
            { "Model AI (Slot 1)", false },
            { "Model AI (Slot 2)", false },
            { "Model Settings General", false },
            { "Settings Menu", false },
            { "X/Y Percentage Adjustment", false },
            { "Theme Settings", false },
            { "Screen Settings", false},
            { "Recoil Config", false},
            { "Weapon Slot System", false },
            { "Weapon Recognition System", false },
            { "Scope Recognition System", false },
            { "Fast Loot Config", false }
        };

        public static Dictionary<string, dynamic> dropdownState = new()
        {
            { "Scope Image Size", "640" },
            { "Weapon Image Size", "640" },
            { "Prediction Method", "Kalman Filter" },
            { "Detection Area Type", "Closest to Center Screen" },
            { "Aiming Boundaries Alignment", "Center" },
            { "Mouse Movement Method", "Mouse Event" },
            { "Screen Capture Method", "GDI+" },
            { "Scope Capture Method", "GDI+" },
            { "Weapon Recognition Method", "Auto Hybrid" },
            { "Scope Recognition Method", "Template Matching" },
            { "Tracer Position", "Bottom" },
            { "Movement Path", "Cubic Bezier" },
            { "ONNX Provider", "Legacy" },
            { "Slot 1 Image Size", "640" },
            { "Slot 2 Image Size", "640" },
            { "Slot 1 Target Class", "Best Confidence" },
            { "Slot 2 Target Class", "Best Confidence" },
            // Slot 1 Keys
            { "Slot 1 Mouse Movement Method", "Mouse Event" },
            { "Slot 1 Movement Path", "Linear" },
            { "Slot 1 Detection Area Type", "Closest to Center Screen" },
            { "Slot 1 Aiming Boundaries Alignment", "Center" }
        };

        public static Dictionary<string, dynamic> colorState = new()
        {
            { "FOV Color", "#FF8080FF"},
            { "Detected Player Color", "#FF00FFFF"},
            { "Crosshair Color", "#FFFFFFFF"},
            { "Theme Color", "#FF722ED1" },
            { "Single Class Color", "#FF0078FF" },
            { "ONNX Head Color", "#FFFF0000" },
            { "ONNX Body Color", "#FF00FF00" },
            { "Engine Head Color", "#FF0078FF" },
            { "Engine Body Color", "#FFFFFF00" }
        };

        public static Dictionary<string, dynamic> filelocationState = new()
        {
            { "ddxoft DLL Location", ""},
            { "Scope Model Location", "bin\\scope_models\\scope.onnx"}
            ,{ "Weapon Model Location", ""}
        };

        public static Dictionary<string, dynamic> modelState = new()
        {
            { "Slot1", "N/A" },
            { "Slot2", "N/A" }
        };
    }
}
