using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Visuality;

internal static class SearchableComboBoxBehavior
{
    public static void Attach(ComboBox comboBox)
    {
        comboBox.IsEditable = true;
        comboBox.IsTextSearchEnabled = false;
        comboBox.StaysOpenOnEdit = true;

        TextBox? editor = null;
        object? committedSelection = comboBox.SelectedItem;
        ComboBoxItem? suggestedContainer = null;
        System.Windows.Window? keyWindow = null;
        bool internalChange = false;
        bool filtering = false;

        static string DisplayText(object? item) => item is ComboBoxItem comboItem
            ? comboItem.Content?.ToString() ?? string.Empty
            : item?.ToString() ?? string.Empty;

        void ClearSuggestion()
        {
            if (suggestedContainer == null) return;
            suggestedContainer.ClearValue(Control.BackgroundProperty);
            suggestedContainer.ClearValue(Control.FontWeightProperty);
            suggestedContainer = null;
        }

        void ShowAll()
        {
            ClearSuggestion();
            comboBox.Items.Filter = null;
            filtering = false;
        }

        void HighlightFirstMatch()
        {
            ClearSuggestion();
            if (!filtering || comboBox.Items.Count == 0 || !comboBox.IsDropDownOpen) return;
            comboBox.Dispatcher.BeginInvoke(() =>
            {
                if (!filtering || comboBox.Items.Count == 0 || !comboBox.IsDropDownOpen) return;
                comboBox.UpdateLayout();
                suggestedContainer = comboBox.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem
                    ?? comboBox.Items[0] as ComboBoxItem;
                if (suggestedContainer == null) return;
                suggestedContainer.Background = new SolidColorBrush(Color.FromRgb(40, 82, 98));
                suggestedContainer.FontWeight = System.Windows.FontWeights.SemiBold;
            }, DispatcherPriority.ContextIdle);
        }

        void CommitFirstMatch(TextBox currentEditor)
        {
            if (!filtering || comboBox.Items.Count == 0) return;
            object item = comboBox.Items[0];
            internalChange = true;
            ShowAll();
            comboBox.SelectedItem = item;
            committedSelection = item;
            string text = DisplayText(item);
            comboBox.Text = text;
            currentEditor.Text = text;
            currentEditor.CaretIndex = text.Length;
            internalChange = false;
            comboBox.IsDropDownOpen = false;
        }

        void HandlePreviewKeyDown(KeyEventArgs e)
        {
            if (!comboBox.IsKeyboardFocusWithin) return;
            if (e.Key == Key.Enter && filtering && comboBox.Items.Count > 0 && editor != null)
            {
                CommitFirstMatch(editor);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                RestoreCommittedSelection();
                comboBox.IsDropDownOpen = false;
                e.Handled = true;
            }
        }

        KeyEventHandler windowKeyHandler = (_, e) => HandlePreviewKeyDown(e);

        void ConnectWindowKeyHandler()
        {
            System.Windows.Window? currentWindow = System.Windows.Window.GetWindow(comboBox);
            if (currentWindow == null || ReferenceEquals(keyWindow, currentWindow)) return;
            if (keyWindow != null) keyWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, windowKeyHandler);
            keyWindow = currentWindow;
            keyWindow.AddHandler(Keyboard.PreviewKeyDownEvent, windowKeyHandler, true);
        }


        void RestoreCommittedSelection()
        {
            if (comboBox.SelectedItem != null || committedSelection == null) return;
            internalChange = true;
            ShowAll();
            comboBox.SelectedItem = committedSelection;
            comboBox.Text = DisplayText(committedSelection);
            internalChange = false;
        }

        void BeginSelection()
        {
            ShowAll();
            comboBox.IsDropDownOpen = true;
            comboBox.Dispatcher.BeginInvoke(() => editor?.SelectAll(), DispatcherPriority.Input);
        }

        void InitializeEditor()
        {
            comboBox.ApplyTemplate();
            TextBox? currentEditor = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox;
            if (currentEditor == null || ReferenceEquals(editor, currentEditor)) return;
            editor = currentEditor;

            currentEditor.PreviewMouseLeftButtonDown += (_, _) => BeginSelection();
            currentEditor.GotKeyboardFocus += (_, _) => BeginSelection();
            currentEditor.TextChanged += (_, _) =>
            {
                if (internalChange || !ReferenceEquals(editor, currentEditor)) return;
                string query = currentEditor.Text.Trim();
                if (comboBox.SelectedItem != null && string.Equals(query, DisplayText(comboBox.SelectedItem), StringComparison.CurrentCultureIgnoreCase)) return;

                internalChange = true;
                comboBox.SelectedItem = null;
                comboBox.Text = query;
                internalChange = false;
                comboBox.Items.Filter = item => DisplayText(item).StartsWith(query, StringComparison.CurrentCultureIgnoreCase);
                filtering = true;
                comboBox.IsDropDownOpen = true;
                currentEditor.CaretIndex = currentEditor.Text.Length;
                HighlightFirstMatch();
            };
            currentEditor.LostKeyboardFocus += (_, _) => comboBox.Dispatcher.BeginInvoke(() =>
            {
                if (!comboBox.IsKeyboardFocusWithin) RestoreCommittedSelection();
            }, DispatcherPriority.Input);
        }

        comboBox.Loaded += (_, _) => comboBox.Dispatcher.BeginInvoke(() =>
        {
            InitializeEditor();
            ConnectWindowKeyHandler();
        }, DispatcherPriority.Loaded);
        comboBox.PreviewKeyDown += (_, e) => HandlePreviewKeyDown(e);
        comboBox.Unloaded += (_, _) =>
        {
            if (keyWindow == null) return;
            keyWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, windowKeyHandler);
            keyWindow = null;
        };
        // Language/theme refreshes can recreate the editable TextBox after Loaded.
        // Reconnect the behavior whenever the ComboBox template swaps that part.
        comboBox.LayoutUpdated += (_, _) => InitializeEditor();
        comboBox.DropDownOpened += (_, _) =>
        {
            InitializeEditor();
            if (!filtering) ShowAll();
            else HighlightFirstMatch();
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (internalChange || comboBox.SelectedItem == null) return;
            committedSelection = comboBox.SelectedItem;
            internalChange = true;
            ShowAll();
            comboBox.Text = DisplayText(committedSelection);
            internalChange = false;
        };
        if (comboBox.IsLoaded) InitializeEditor();
    }
}
