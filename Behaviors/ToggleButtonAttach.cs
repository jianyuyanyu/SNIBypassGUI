using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SNIBypassGUI.Behaviors
{
    public static class ToggleButtonAttach
    {
        #region IsAutoFold
        [AttachedPropertyBrowsableForType(typeof(ToggleButton))]
        public static bool GetIsAutoFold(ToggleButton control) => (bool)control.GetValue(IsAutoFoldProperty);

        public static void SetIsAutoFold(ToggleButton control, bool value)
        {
            control.SetValue(IsAutoFoldProperty, value);
        }

        public static readonly DependencyProperty IsAutoFoldProperty =
            DependencyProperty.RegisterAttached("IsAutoFold", typeof(bool), typeof(ToggleButtonAttach),
                new PropertyMetadata(false, ToggleButtonChanged));

        private static void ToggleButtonChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not ToggleButton control)
                return;

            if ((bool)e.NewValue)
            {
                control.Loaded += Control_Loaded;
                control.MouseLeave += Control_MouseLeave;
                control.Checked += Control_Checked;
                control.Unchecked += Control_Checked;
            }
            else
            {
                control.Loaded -= Control_Loaded;
                control.Checked -= Control_Checked;
                control.Unchecked -= Control_Checked;
                VisualStateManager.GoToState(control, "Normal", false);
            }
        }

        private static void Control_Loaded(object sender, RoutedEventArgs e)
        {
            var control = (ToggleButton)sender;
            UpdateVisualState(control);
        }

        private static void Control_Checked(object sender, RoutedEventArgs e)
        {
            var control = (ToggleButton)sender;
            if (control.IsMouseOver) return;
            UpdateVisualState(control);
        }

        private static void Control_MouseLeave(object sender, MouseEventArgs e)
        {
            var control = (ToggleButton)sender;
            UpdateVisualState(control);
        }

        private static void UpdateVisualState(ToggleButton control)
        {
            var state = control.IsChecked == true ? "MouseLeaveChecked" : "MouseLeaveUnChecked";
            if (control.IsMouseOver) state = "MouseOver";
            VisualStateManager.GoToState(control, state, true);
        }
        #endregion
    }
}
