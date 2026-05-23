using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KlevaDeploy.Behaviors;

public static class ScrollResetBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollResetBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        if (e.NewValue is true)
        {
            sv.Loaded += OnLoaded;
            sv.IsVisibleChanged += OnIsVisibleChanged;
        }
        else
        {
            sv.Loaded -= OnLoaded;
            sv.IsVisibleChanged -= OnIsVisibleChanged;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        ResetScroll(sv);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.IsVisible) ResetScroll(sv);
    }

    private static void ResetScroll(ScrollViewer sv)
    {
        sv.Dispatcher.BeginInvoke(() => sv.ScrollToTop(), DispatcherPriority.Loaded);
        sv.Dispatcher.BeginInvoke(() => sv.ScrollToTop(), DispatcherPriority.Background);
    }
}

