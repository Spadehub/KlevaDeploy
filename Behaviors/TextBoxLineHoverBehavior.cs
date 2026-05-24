using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KlevaDeploy.Behaviors;

public static class TextBoxLineHoverBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TextBoxLineHoverBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached(
            "HoverBrush",
            typeof(Brush),
            typeof(TextBoxLineHoverBehavior),
            new PropertyMetadata(Brushes.Transparent, OnHoverBrushChanged));

    public static readonly DependencyProperty HoverCharacterIndexProperty =
        DependencyProperty.RegisterAttached(
            "HoverCharacterIndex",
            typeof(int),
            typeof(TextBoxLineHoverBehavior),
            new PropertyMetadata(-1));

    private static readonly DependencyProperty AdornerProperty =
        DependencyProperty.RegisterAttached(
            "Adorner",
            typeof(LineHoverAdorner),
            typeof(TextBoxLineHoverBehavior),
            new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetHoverBrush(DependencyObject element, Brush value) =>
        element.SetValue(HoverBrushProperty, value);

    public static Brush GetHoverBrush(DependencyObject element) =>
        (Brush)element.GetValue(HoverBrushProperty);

    public static void SetHoverCharacterIndex(DependencyObject element, int value) =>
        element.SetValue(HoverCharacterIndexProperty, value);

    public static int GetHoverCharacterIndex(DependencyObject element) =>
        (int)element.GetValue(HoverCharacterIndexProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;

        if ((bool)e.NewValue)
        {
            textBox.Loaded += OnLoaded;
            textBox.Unloaded += OnUnloaded;
            textBox.PreviewMouseMove += OnMouseMove;
            textBox.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            textBox.MouseLeave += OnMouseLeave;
            textBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        }
        else
        {
            textBox.Loaded -= OnLoaded;
            textBox.Unloaded -= OnUnloaded;
            textBox.PreviewMouseMove -= OnMouseMove;
            textBox.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
            textBox.MouseLeave -= OnMouseLeave;
            textBox.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            DetachAdorner(textBox);
        }
    }

    private static void OnHoverBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox) return;
        var adorner = (LineHoverAdorner?)textBox.GetValue(AdornerProperty);
        if (adorner is null) return;
        adorner.HoverBrush = (Brush)e.NewValue;
        adorner.InvalidateVisual();
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        AttachAdorner(textBox);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        DetachAdorner(textBox);
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        UpdateHover(textBox, e.GetPosition(textBox));
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        UpdateHover(textBox, e.GetPosition(textBox));
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var adorner = (LineHoverAdorner?)textBox.GetValue(AdornerProperty);
        textBox.SetValue(HoverCharacterIndexProperty, -1);
        adorner?.SetHoverRect(null);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var adorner = (LineHoverAdorner?)textBox.GetValue(AdornerProperty);
        if (adorner is null) return;
        adorner.InvalidateVisual();
    }

    private static void AttachAdorner(TextBox textBox)
    {
        if ((LineHoverAdorner?)textBox.GetValue(AdornerProperty) is not null) return;

        var layer = AdornerLayer.GetAdornerLayer(textBox);
        if (layer is null) return;

        var adorner = new LineHoverAdorner(textBox) { HoverBrush = GetHoverBrush(textBox) };
        layer.Add(adorner);
        textBox.SetValue(AdornerProperty, adorner);
    }

    private static void UpdateHover(TextBox textBox, Point position)
    {
        var adorner = (LineHoverAdorner?)textBox.GetValue(AdornerProperty);

        var index = textBox.GetCharacterIndexFromPoint(position, true);
        if (index < 0)
        {
            textBox.SetValue(HoverCharacterIndexProperty, -1);
            adorner?.SetHoverRect(null);
            return;
        }

        textBox.SetValue(HoverCharacterIndexProperty, index);

        if (adorner is null) return;

        var lineIndex = textBox.GetLineIndexFromCharacterIndex(index);
        if (lineIndex < 0)
        {
            adorner.SetHoverRect(null);
            return;
        }

        var firstChar = textBox.GetCharacterIndexFromLineIndex(lineIndex);
        var rectFirst = textBox.GetRectFromCharacterIndex(firstChar);

        var height = rectFirst.Height;
        if (height <= 0)
            height = Math.Max(12, textBox.FontSize * 1.45);

        var y = rectFirst.Y;
        var rect = new Rect(0, y, Math.Max(0, textBox.ActualWidth), height);
        adorner.SetHoverRect(rect);
    }

    private static void DetachAdorner(TextBox textBox)
    {
        var adorner = (LineHoverAdorner?)textBox.GetValue(AdornerProperty);
        if (adorner is null) return;

        var layer = AdornerLayer.GetAdornerLayer(textBox);
        layer?.Remove(adorner);
        textBox.ClearValue(AdornerProperty);
    }

    private sealed class LineHoverAdorner : Adorner
    {
        private Rect? _hoverRect;

        public Brush HoverBrush { get; set; } = Brushes.Transparent;

        public LineHoverAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void SetHoverRect(Rect? rect)
        {
            if (_hoverRect == rect) return;
            _hoverRect = rect;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_hoverRect is null) return;
            if (HoverBrush is null) return;
            drawingContext.DrawRectangle(HoverBrush, null, _hoverRect.Value);
        }
    }
}
