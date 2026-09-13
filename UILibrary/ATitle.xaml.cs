using Aimmy2.Class;

namespace Aimmy2.UILibrary
{
    /// <summary>
    /// Interaction logic for ATitle.xaml
    /// </summary>
    public partial class ATitle : System.Windows.Controls.UserControl
    {
        public ATitle(string Text, bool MinimizableMenu = false)
        {
            InitializeComponent();
            Loaded += (_, _) => global::Other.UiLanguage.RefreshTree(this);

            LabelTitle.Content = Text;
            global::Other.UiLanguage.Localize(LabelTitle);

            if (MinimizableMenu)
            {
                Minimize.Visibility = System.Windows.Visibility.Visible;
                SetMinimized(Dictionary.minimizeState.TryGetValue(Text, out var savedState) && Convert.ToBoolean(savedState));
            }
        }

        public void SetMinimized(bool minimized)
        {
            // E710 = Add (+), E921 = Remove (-). The owning panel is the single
            // source of truth for state; this control only renders that state.
            Minimize.Content = minimized ? "\xE710" : "\xE921";
            Minimize.ToolTip = minimized ? "Mở rộng" : "Thu gọn";
            Minimize.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
                minimized ? "Mở rộng mục" : "Thu gọn mục");
        }
    }
}
