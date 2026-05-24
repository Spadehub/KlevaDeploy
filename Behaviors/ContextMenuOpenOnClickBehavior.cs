using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace KlevaDeploy.Behaviors;

public static class ContextMenuOpenOnClickBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ContextMenuOpenOnClickBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase btn) return;

        if (e.NewValue is true)
            btn.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        else
            btn.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ButtonBase btn) return;
        var menu = btn.ContextMenu;
        if (menu is null) return;

        menu.PlacementTarget = btn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
