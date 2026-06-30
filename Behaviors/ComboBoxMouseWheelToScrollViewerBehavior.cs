using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KlevaDeploy.Behaviors;

public static class ComboBoxMouseWheelToScrollViewerBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ComboBoxMouseWheelToScrollViewerBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static readonly MouseWheelEventHandler Handler = OnPreviewMouseWheel;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox cb) return;

        if ((bool)e.NewValue)
            cb.PreviewMouseWheel += Handler;
        else
            cb.PreviewMouseWheel -= Handler;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.IsDropDownOpen) return;

        var scrollViewer = FindAncestorScrollViewer(cb);
        if (scrollViewer is null) return;

        var detents = Math.Abs(e.Delta) / 120;
        if (detents == 0) detents = 1;

        var lines = SystemParameters.WheelScrollLines;
        if (lines <= 0) lines = 3;

        var steps = detents * lines;
        for (var i = 0; i < steps; i++)
        {
            if (e.Delta > 0)
                scrollViewer.LineUp();
            else
                scrollViewer.LineDown();
        }

        e.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current != null)
        {
            if (current is ScrollViewer sv) return sv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
