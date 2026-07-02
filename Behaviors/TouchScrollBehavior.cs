using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace KlevaDeploy.Behaviors;

public static class TouchScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TouchScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(TouchScrollState),
            typeof(TouchScrollBehavior),
            new PropertyMetadata(null));

    private const double DragThreshold = 8d;

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static TouchScrollState GetOrCreateState(DependencyObject obj)
    {
        if (obj.GetValue(StateProperty) is not TouchScrollState state)
        {
            state = new TouchScrollState();
            obj.SetValue(StateProperty, state);
        }

        return state;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv)
            return;

        if ((bool)e.NewValue)
        {
            ConfigureScrollViewer(sv);
            sv.Loaded += OnLoaded;
            sv.ScrollChanged += OnScrollChanged;
            sv.PreviewTouchDown += OnPreviewTouchDown;
            sv.PreviewTouchMove += OnPreviewTouchMove;
            sv.PreviewTouchUp += OnPreviewTouchUp;
            sv.LostTouchCapture += OnLostTouchCapture;
            sv.ManipulationBoundaryFeedback += OnManipulationBoundaryFeedback;
        }
        else
        {
            sv.Loaded -= OnLoaded;
            sv.ScrollChanged -= OnScrollChanged;
            sv.PreviewTouchDown -= OnPreviewTouchDown;
            sv.PreviewTouchMove -= OnPreviewTouchMove;
            sv.PreviewTouchUp -= OnPreviewTouchUp;
            sv.LostTouchCapture -= OnLostTouchCapture;
            sv.ManipulationBoundaryFeedback -= OnManipulationBoundaryFeedback;
            objClearState(sv);
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer sv)
            ConfigureScrollViewer(sv);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
            ConfigureScrollViewer(sv);
    }

    private static void ConfigureScrollViewer(ScrollViewer sv)
    {
        sv.IsManipulationEnabled = true;
        sv.PanningRatio = 1d;
        sv.PanningDeceleration = 0.001;
        sv.SetValue(Stylus.IsFlicksEnabledProperty, false);

        var vertical = sv.ScrollableHeight > 0 || sv.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
        var horizontal = sv.ScrollableWidth > 0 || sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;

        sv.PanningMode = (vertical, horizontal) switch
        {
            (true, true) => PanningMode.Both,
            (true, false) => PanningMode.VerticalFirst,
            (false, true) => PanningMode.HorizontalFirst,
            _ => PanningMode.None
        };
    }

    private static void OnPreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        var state = GetOrCreateState(sv);
        if (state.ActiveTouchDevice is not null)
            return;

        state.ActiveTouchDevice = e.TouchDevice;
        state.Origin = e.GetTouchPoint(sv).Position;
        state.StartHorizontalOffset = sv.HorizontalOffset;
        state.StartVerticalOffset = sv.VerticalOffset;
        state.IsDragging = false;
    }

    private static void OnPreviewTouchMove(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        var state = GetOrCreateState(sv);
        if (!ReferenceEquals(state.ActiveTouchDevice, e.TouchDevice))
            return;

        var current = e.GetTouchPoint(sv).Position;
        var delta = current - state.Origin;

        if (!state.IsDragging)
        {
            if (!CanScroll(sv) || !ExceedsThreshold(delta))
                return;

            state.IsDragging = true;
            e.TouchDevice.Capture(sv);
        }

        if (!state.IsDragging)
            return;

        if (CanScrollVertically(sv))
            sv.ScrollToVerticalOffset(CoerceOffset(state.StartVerticalOffset - delta.Y, 0, sv.ScrollableHeight));

        if (CanScrollHorizontally(sv))
            sv.ScrollToHorizontalOffset(CoerceOffset(state.StartHorizontalOffset - delta.X, 0, sv.ScrollableWidth));

        e.Handled = true;
    }

    private static void OnPreviewTouchUp(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        var state = GetOrCreateState(sv);
        if (!ReferenceEquals(state.ActiveTouchDevice, e.TouchDevice))
            return;

        if (state.IsDragging)
            e.Handled = true;

        ReleaseTouch(sv, e.TouchDevice, state);
    }

    private static void OnLostTouchCapture(object? sender, TouchEventArgs e)
    {
        if (sender is not ScrollViewer sv)
            return;

        var state = GetOrCreateState(sv);
        if (ReferenceEquals(state.ActiveTouchDevice, e.TouchDevice))
            ResetState(state);
    }

    private static void OnManipulationBoundaryFeedback(object? sender, ManipulationBoundaryFeedbackEventArgs e)
    {
        e.Handled = true;
    }

    private static bool CanScroll(ScrollViewer sv) => CanScrollVertically(sv) || CanScrollHorizontally(sv);

    private static bool CanScrollVertically(ScrollViewer sv) => sv.ScrollableHeight > 0 && sv.PanningMode is PanningMode.Both or PanningMode.VerticalFirst or PanningMode.VerticalOnly;

    private static bool CanScrollHorizontally(ScrollViewer sv) => sv.ScrollableWidth > 0 && sv.PanningMode is PanningMode.Both or PanningMode.HorizontalFirst or PanningMode.HorizontalOnly;

    private static bool ExceedsThreshold(Vector delta)
    {
        return Math.Abs(delta.X) >= DragThreshold || Math.Abs(delta.Y) >= DragThreshold;
    }

    private static double CoerceOffset(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static void ReleaseTouch(ScrollViewer sv, TouchDevice device, TouchScrollState state)
    {
        if (ReferenceEquals(Mouse.Captured, sv))
            Mouse.Capture(null);

        if (ReferenceEquals(device.Captured, sv))
            device.Capture(null);

        ResetState(state);
    }

    private static void ResetState(TouchScrollState state)
    {
        state.ActiveTouchDevice = null;
        state.IsDragging = false;
        state.Origin = default;
        state.StartHorizontalOffset = 0;
        state.StartVerticalOffset = 0;
    }

    private static void objClearState(DependencyObject obj)
    {
        obj.ClearValue(StateProperty);
    }

    private sealed class TouchScrollState
    {
        public TouchDevice? ActiveTouchDevice { get; set; }
        public Point Origin { get; set; }
        public double StartHorizontalOffset { get; set; }
        public double StartVerticalOffset { get; set; }
        public bool IsDragging { get; set; }
    }
}
