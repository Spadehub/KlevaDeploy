using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace KlevaDeploy.Behaviors;

public static class ScrollFadeBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollFadeBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty AdornerProperty =
        DependencyProperty.RegisterAttached(
            "Adorner",
            typeof(ScrollFadeAdorner),
            typeof(ScrollFadeBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static ScrollFadeAdorner? GetAdorner(DependencyObject obj) => (ScrollFadeAdorner?)obj.GetValue(AdornerProperty);
    private static void SetAdorner(DependencyObject obj, ScrollFadeAdorner? value) => obj.SetValue(AdornerProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        if (e.NewValue is true)
        {
            sv.Loaded += OnLoaded;
            sv.Unloaded += OnUnloaded;
            sv.ScrollChanged += OnScrollChanged;
            sv.SizeChanged += OnSizeChanged;
        }
        else
        {
            sv.Loaded -= OnLoaded;
            sv.Unloaded -= OnUnloaded;
            sv.ScrollChanged -= OnScrollChanged;
            sv.SizeChanged -= OnSizeChanged;
            DetachAdorner(sv);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        AttachAdorner(sv);
        InvalidateAdorner(sv);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        DetachAdorner(sv);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        InvalidateAdorner(sv);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        InvalidateAdorner(sv);
    }

    private static void AttachAdorner(ScrollViewer sv)
    {
        if (GetAdorner(sv) is not null) return;

        var layer = AdornerLayer.GetAdornerLayer(sv);
        if (layer is null) return;

        var adorner = new ScrollFadeAdorner(sv) { IsHitTestVisible = false };
        SetAdorner(sv, adorner);
        layer.Add(adorner);
    }

    private static void DetachAdorner(ScrollViewer sv)
    {
        var adorner = GetAdorner(sv);
        if (adorner is null) return;

        var layer = AdornerLayer.GetAdornerLayer(sv);
        if (layer is not null)
        {
            layer.Remove(adorner);
        }

        SetAdorner(sv, null);
    }

    private static void InvalidateAdorner(ScrollViewer sv)
    {
        var adorner = GetAdorner(sv);
        if (adorner is null) return;
        adorner.InvalidateVisual();
    }

    private sealed class ScrollFadeAdorner : Adorner
    {
        private readonly ScrollViewer _sv;

        public ScrollFadeAdorner(ScrollViewer adornedElement) : base(adornedElement)
        {
            _sv = adornedElement;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!TryGetContentViewport(_sv, out var viewport))
            {
                var width = _sv.RenderSize.Width;
                var height = _sv.RenderSize.Height;

                if (_sv.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                    width = Math.Max(0, width - GetVerticalScrollBarWidth(_sv));

                if (_sv.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
                    height = Math.Max(0, height - GetHorizontalScrollBarHeight(_sv));

                viewport = new Rect(0, 0, width, height);
            }

            if (viewport.Width <= 0 || viewport.Height <= 0) return;

            var size = TryGetResourceDouble(_sv, "ScrollFadeSize", 16);
            var opacity = TryGetResourceDouble(_sv, "ScrollFadeOpacity", 0.75);
            var overlayBrush = FindOverlayBrush(_sv);

            dc.PushClip(new RectangleGeometry(viewport));

            if (_sv.ScrollableHeight > 0)
            {
                var showTop = _sv.VerticalOffset > 0;
                var showBottom = _sv.VerticalOffset < _sv.ScrollableHeight;

                if (showTop)
                {
                    var rect = new Rect(viewport.X, viewport.Y, viewport.Width, size);
                    dc.PushOpacity(opacity);
                    dc.PushOpacityMask(CreateOpacityMask(Direction.Top));
                    dc.DrawRectangle(overlayBrush, null, rect);
                    dc.Pop();
                    dc.Pop();
                }

                if (showBottom)
                {
                    var rect = new Rect(viewport.X, viewport.Bottom - size, viewport.Width, size);
                    dc.PushOpacity(opacity);
                    dc.PushOpacityMask(CreateOpacityMask(Direction.Bottom));
                    dc.DrawRectangle(overlayBrush, null, rect);
                    dc.Pop();
                    dc.Pop();
                }
            }

            if (_sv.ScrollableWidth > 0)
            {
                var showLeft = _sv.HorizontalOffset > 0;
                var showRight = _sv.HorizontalOffset < _sv.ScrollableWidth;

                if (showLeft)
                {
                    var rect = new Rect(viewport.X, viewport.Y, size, viewport.Height);
                    dc.PushOpacity(opacity);
                    dc.PushOpacityMask(CreateOpacityMask(Direction.Left));
                    dc.DrawRectangle(overlayBrush, null, rect);
                    dc.Pop();
                    dc.Pop();
                }

                if (showRight)
                {
                    var rect = new Rect(viewport.Right - size, viewport.Y, size, viewport.Height);
                    dc.PushOpacity(opacity);
                    dc.PushOpacityMask(CreateOpacityMask(Direction.Right));
                    dc.DrawRectangle(overlayBrush, null, rect);
                    dc.Pop();
                    dc.Pop();
                }
            }

            dc.Pop();
        }

        private enum Direction { Top, Bottom, Left, Right }

        private static bool TryGetContentViewport(ScrollViewer sv, out Rect viewport)
        {
            var presenter = FindVisualChild<ScrollContentPresenter>(sv);
            if (presenter is null)
            {
                viewport = default;
                return false;
            }

            if (presenter.RenderSize.Width <= 0 || presenter.RenderSize.Height <= 0)
            {
                viewport = default;
                return false;
            }

            var topLeft = presenter.TranslatePoint(new Point(0, 0), sv);
            viewport = new Rect(topLeft.X, topLeft.Y, presenter.RenderSize.Width, presenter.RenderSize.Height);
            return viewport.Width > 0 && viewport.Height > 0;
        }

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;
                var found = FindVisualChild<T>(child);
                if (found is not null) return found;
            }

            return null;
        }

        private static double GetVerticalScrollBarWidth(ScrollViewer sv)
        {
            if (sv.Template?.FindName("PART_VerticalScrollBar", sv) is System.Windows.Controls.Primitives.ScrollBar bar)
            {
                if (bar.RenderSize.Width > 0) return bar.RenderSize.Width;
                if (!double.IsNaN(bar.ActualWidth) && bar.ActualWidth > 0) return bar.ActualWidth;
                if (!double.IsNaN(bar.Width) && bar.Width > 0) return bar.Width;
            }

            return SystemParameters.VerticalScrollBarWidth;
        }

        private static double GetHorizontalScrollBarHeight(ScrollViewer sv)
        {
            if (sv.Template?.FindName("PART_HorizontalScrollBar", sv) is System.Windows.Controls.Primitives.ScrollBar bar)
            {
                if (bar.RenderSize.Height > 0) return bar.RenderSize.Height;
                if (!double.IsNaN(bar.ActualHeight) && bar.ActualHeight > 0) return bar.ActualHeight;
                if (!double.IsNaN(bar.Height) && bar.Height > 0) return bar.Height;
            }

            return SystemParameters.HorizontalScrollBarHeight;
        }

        private static Brush FindOverlayBrush(FrameworkElement scope)
        {
            if (scope.TryFindResource("ScrollFadeOverlayBrush") is Brush b)
            {
                return b.CloneCurrentValue();
            }

            if (scope.TryFindResource("SurfaceBrush") is Brush surface)
            {
                return surface.CloneCurrentValue();
            }

            return Brushes.Transparent;
        }

        private static Brush CreateOpacityMask(Direction direction)
        {
            var b = new LinearGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Colors.Transparent, 1),
                ]
            };

            switch (direction)
            {
                case Direction.Top:
                    b.StartPoint = new Point(0, 0);
                    b.EndPoint = new Point(0, 1);
                    return b;
                case Direction.Bottom:
                    b.StartPoint = new Point(0, 1);
                    b.EndPoint = new Point(0, 0);
                    return b;
                case Direction.Left:
                    b.StartPoint = new Point(0, 0);
                    b.EndPoint = new Point(1, 0);
                    return b;
                case Direction.Right:
                    b.StartPoint = new Point(1, 0);
                    b.EndPoint = new Point(0, 0);
                    return b;
                default:
                    return b;
            }
        }

        private static double TryGetResourceDouble(FrameworkElement scope, object key, double fallback)
        {
            var value = scope.TryFindResource(key);
            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                _ => fallback
            };
        }
    }
}

