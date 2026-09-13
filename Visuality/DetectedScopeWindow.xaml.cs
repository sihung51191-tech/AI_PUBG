using Aimmy2.Class;
using System.Windows;
using Aimmy2.AILogic.Weapons;
using Aimmy2.AILogic.Recognition;
using System.Windows.Media;

namespace Visuality
{
    public partial class DetectedScopeWindow : Window
    {
        public DetectedScopeWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => global::Other.UiLanguage.RefreshTree(this);
            
            // Position at top-right corner
            this.Left = DisplayManager.ScreenLeft + DisplayManager.ScreenWidth - this.Width - 20;
            this.Top = 20;
            
            // Initially hidden
            // Initially hidden
            this.Visibility = Visibility.Collapsed;
            this.SourceInitialized += (s, e) => MakeClickThrough();
        }

        public void SetScalePercent(double percent)
        {
            Dispatcher.Invoke(() =>
            {
                double scale = Math.Clamp(percent, 60, 120) / 100.0;
                OverlayRoot.LayoutTransform = new ScaleTransform(scale, scale);
                Dispatcher.BeginInvoke(ForceReposition, System.Windows.Threading.DispatcherPriority.Loaded);
            });
        }

        public void SetOpacityPercent(double percent)
        {
            Dispatcher.Invoke(() => OverlayRoot.Opacity = Math.Clamp(percent, 20, 100) / 100.0);
        }

        private void MakeClickThrough()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            ClickThroughOverlay.MakeClickThrough(hwnd);
        }

        public void UpdateSlot1(string scopeName)
        {
            Dispatcher.Invoke(() =>
            {
                Slot1Text.Text = scopeName;
            });
        }

        public void UpdateRecognition(WeaponSlotState slot1, WeaponSlotState slot2, int activeSlot, string profile)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SetIfChanged(Slot1WeaponText, slot1.DetectedWeaponName);
                SetIfChanged(Slot1Text, slot1.DetectedScopeName);
                SetIfChanged(Slot1DetailText, $"{ShortMethod(slot1.WeaponRecognitionMethod)} {slot1.WeaponScore:0.00} · {ShortMethod(slot1.ScopeRecognitionMethod)} {slot1.ScopeScore:0.00}");
                SetIfChanged(Slot2WeaponText, slot2.DetectedWeaponName);
                SetIfChanged(Slot2Text, slot2.DetectedScopeName);
                SetIfChanged(Slot2DetailText, $"{ShortMethod(slot2.WeaponRecognitionMethod)} {slot2.WeaponScore:0.00} · {ShortMethod(slot2.ScopeRecognitionMethod)} {slot2.ScopeScore:0.00}");
                SetIfChanged(ActiveSlotText, $"Đang dùng S{activeSlot}");
                SetIfChanged(ActiveProfileText, profile);
            });
        }
        private static string ShortMethod(RecognitionMethod value) => value switch
        {
            RecognitionMethod.TemplateMatching => "TPL",
            RecognitionMethod.OrbFeatureMatching => "ORB",
            RecognitionMethod.SiftFeatureMatching => "SIFT",
            RecognitionMethod.AiModel => "AI",
            RecognitionMethod.AutoHybrid => "AUTO",
            RecognitionMethod.Ocr => "OCR",
            _ => value.ToString()
        };
        private static void SetIfChanged(System.Windows.Controls.TextBlock text, string value) { if (text.Text != value) text.Text = value; }

        public void UpdateSlot2(string scopeName)
        {
            Dispatcher.Invoke(() =>
            {
                Slot2Text.Text = scopeName;
            });
        }

        public void UpdateActiveSlot(int slotNumber)
        {
            Dispatcher.Invoke(() =>
            {
                ActiveSlotText.Text = $"Đang dùng S{slotNumber}";
            });
        }

        public void UpdateRecoilAdj(string value)
        {
            Dispatcher.Invoke(() =>
            {
                RecoilAdjText.Text = value;
            });
        }

        public void ForceReposition()
        {
            Dispatcher.Invoke(() =>
            {
                this.Left = DisplayManager.ScreenLeft + DisplayManager.ScreenWidth - this.Width - 20;
                this.Top = 20;
            });
        }

        public void Show(bool show)
        {
            Dispatcher.Invoke(() =>
            {
                this.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) ForceReposition();
            });
        }
    }
}
