using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using KlevaDeploy.Models;

namespace KlevaDeploy.Behaviors;

public static class DragDropReorder
{
    private static ListBox? _activeListBox;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static readonly DependencyProperty ReorderCommandProperty =
        DependencyProperty.RegisterAttached(
            "ReorderCommand",
            typeof(ICommand),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    public static void SetReorderCommand(DependencyObject element, ICommand value) => element.SetValue(ReorderCommandProperty, value);
    public static ICommand GetReorderCommand(DependencyObject element) => (ICommand)element.GetValue(ReorderCommandProperty);

    public static readonly DependencyProperty IsDropTargetProperty =
        DependencyProperty.RegisterAttached(
            "IsDropTarget",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    public static void SetIsDropTarget(DependencyObject element, bool value) => element.SetValue(IsDropTargetProperty, value);
    public static bool GetIsDropTarget(DependencyObject element) => (bool)element.GetValue(IsDropTargetProperty);

    public static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.RegisterAttached(
            "IsDragging",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    public static void SetIsDragging(DependencyObject element, bool value) => element.SetValue(IsDraggingProperty, value);
    public static bool GetIsDragging(DependencyObject element) => (bool)element.GetValue(IsDraggingProperty);

    private static readonly DependencyProperty DragStartPointProperty =
        DependencyProperty.RegisterAttached(
            "DragStartPoint",
            typeof(Point),
            typeof(DragDropReorder),
            new PropertyMetadata(default(Point)));

    private static void SetDragStartPoint(DependencyObject element, Point value) => element.SetValue(DragStartPointProperty, value);
    private static Point GetDragStartPoint(DependencyObject element) => (Point)element.GetValue(DragStartPointProperty);

    private static readonly DependencyProperty DraggedItemProperty =
        DependencyProperty.RegisterAttached(
            "DraggedItem",
            typeof(ProcessSelectionItem),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetDraggedItem(DependencyObject element, ProcessSelectionItem? value) => element.SetValue(DraggedItemProperty, value);
    private static ProcessSelectionItem? GetDraggedItem(DependencyObject element) => (ProcessSelectionItem?)element.GetValue(DraggedItemProperty);

    private static readonly DependencyProperty DraggedContainerProperty =
        DependencyProperty.RegisterAttached(
            "DraggedContainer",
            typeof(ListBoxItem),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetDraggedContainer(DependencyObject element, ListBoxItem? value) => element.SetValue(DraggedContainerProperty, value);
    private static ListBoxItem? GetDraggedContainer(DependencyObject element) => (ListBoxItem?)element.GetValue(DraggedContainerProperty);

    private static readonly DependencyProperty IsDragInProgressProperty =
        DependencyProperty.RegisterAttached(
            "IsDragInProgress",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    private static void SetIsDragInProgress(DependencyObject element, bool value) => element.SetValue(IsDragInProgressProperty, value);
    private static bool GetIsDragInProgress(DependencyObject element) => (bool)element.GetValue(IsDragInProgressProperty);

    private static readonly DependencyProperty CurrentPointerPointProperty =
        DependencyProperty.RegisterAttached(
            "CurrentPointerPoint",
            typeof(Point),
            typeof(DragDropReorder),
            new PropertyMetadata(default(Point)));

    private static void SetCurrentPointerPoint(DependencyObject element, Point value) => element.SetValue(CurrentPointerPointProperty, value);
    private static Point GetCurrentPointerPoint(DependencyObject element) => (Point)element.GetValue(CurrentPointerPointProperty);

    private static readonly DependencyProperty DragAdornerProperty =
        DependencyProperty.RegisterAttached(
            "DragAdorner",
            typeof(DragGhostAdorner),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetDragAdorner(DependencyObject element, DragGhostAdorner? value) => element.SetValue(DragAdornerProperty, value);
    private static DragGhostAdorner? GetDragAdorner(DependencyObject element) => (DragGhostAdorner?)element.GetValue(DragAdornerProperty);

    private static readonly DependencyProperty DropIndicatorAdornerProperty =
        DependencyProperty.RegisterAttached(
            "DropIndicatorAdorner",
            typeof(DropIndicatorAdorner),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetDropIndicatorAdorner(DependencyObject element, DropIndicatorAdorner? value) => element.SetValue(DropIndicatorAdornerProperty, value);
    private static DropIndicatorAdorner? GetDropIndicatorAdorner(DependencyObject element) => (DropIndicatorAdorner?)element.GetValue(DropIndicatorAdornerProperty);

    private static readonly DependencyProperty LastReorderTargetProperty =
        DependencyProperty.RegisterAttached(
            "LastReorderTarget",
            typeof(ProcessSelectionItem),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetLastReorderTarget(DependencyObject element, ProcessSelectionItem? value) => element.SetValue(LastReorderTargetProperty, value);
    private static ProcessSelectionItem? GetLastReorderTarget(DependencyObject element) => (ProcessSelectionItem?)element.GetValue(LastReorderTargetProperty);

    private static readonly DependencyProperty LastReorderInsertAfterProperty =
        DependencyProperty.RegisterAttached(
            "LastReorderInsertAfter",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    private static void SetLastReorderInsertAfter(DependencyObject element, bool value) => element.SetValue(LastReorderInsertAfterProperty, value);
    private static bool GetLastReorderInsertAfter(DependencyObject element) => (bool)element.GetValue(LastReorderInsertAfterProperty);

    private static readonly DependencyProperty OriginalIndexProperty =
        DependencyProperty.RegisterAttached(
            "OriginalIndex",
            typeof(int),
            typeof(DragDropReorder),
            new PropertyMetadata(-1));

    private static void SetOriginalIndex(DependencyObject element, int value) => element.SetValue(OriginalIndexProperty, value);
    private static int GetOriginalIndex(DependencyObject element) => (int)element.GetValue(OriginalIndexProperty);

    private static readonly DependencyProperty HasRestoredOriginalOrderProperty =
        DependencyProperty.RegisterAttached(
            "HasRestoredOriginalOrder",
            typeof(bool),
            typeof(DragDropReorder),
            new PropertyMetadata(false));

    private static void SetHasRestoredOriginalOrder(DependencyObject element, bool value) => element.SetValue(HasRestoredOriginalOrderProperty, value);
    private static bool GetHasRestoredOriginalOrder(DependencyObject element) => (bool)element.GetValue(HasRestoredOriginalOrderProperty);

    private static readonly DependencyProperty DropTargetContainerProperty =
        DependencyProperty.RegisterAttached(
            "DropTargetContainer",
            typeof(ListBoxItem),
            typeof(DragDropReorder),
            new PropertyMetadata(null));

    private static void SetDropTargetContainer(DependencyObject element, ListBoxItem? value) => element.SetValue(DropTargetContainerProperty, value);
    private static ListBoxItem? GetDropTargetContainer(DependencyObject element) => (ListBoxItem?)element.GetValue(DropTargetContainerProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove += OnPreviewMouseMove;
            listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            listBox.LostMouseCapture += OnLostMouseCapture;
            listBox.TouchDown += OnTouchDown;
            listBox.TouchMove += OnTouchMove;
            listBox.TouchUp += OnTouchUp;
        }
        else
        {
            listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseMove -= OnPreviewMouseMove;
            listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            listBox.LostMouseCapture -= OnLostMouseCapture;
            listBox.TouchDown -= OnTouchDown;
            listBox.TouchMove -= OnTouchMove;
            listBox.TouchUp -= OnTouchUp;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        SetDragStartPoint(listBox, e.GetPosition(listBox));
        var draggedItem = GetItemUnderPointer(listBox, e.GetPosition(listBox));
        SetDraggedItem(listBox, draggedItem);
        SetDraggedContainer(listBox, draggedItem is null ? null : (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(draggedItem));
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(listBox);
            return;
        }

        var point = e.GetPosition(listBox);
        SetCurrentPointerPoint(listBox, point);

        if (!GetIsDragInProgress(listBox))
        {
            var start = GetDragStartPoint(listBox);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginDrag(listBox);
        }

        if (GetIsDragInProgress(listBox))
        {
            UpdateReorder(listBox, point);
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            EndDrag(listBox);
        }
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            EndDrag(listBox);
        }
    }

    private static void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var point = e.GetTouchPoint(listBox).Position;
        SetDragStartPoint(listBox, point);
        SetCurrentPointerPoint(listBox, point);

        var draggedItem = GetItemUnderPointer(listBox, point);
        SetDraggedItem(listBox, draggedItem);
        SetDraggedContainer(listBox, draggedItem is null ? null : (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(draggedItem));
    }

    private static void OnTouchMove(object? sender, TouchEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var point = e.GetTouchPoint(listBox).Position;
        SetCurrentPointerPoint(listBox, point);

        if (!GetIsDragInProgress(listBox))
        {
            var start = GetDragStartPoint(listBox);
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            BeginDrag(listBox);
        }

        if (GetIsDragInProgress(listBox))
        {
            UpdateReorder(listBox, point);
        }
    }

    private static void OnTouchUp(object? sender, TouchEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            EndDrag(listBox);
        }
    }

    private static void BeginDrag(ListBox listBox)
    {
        var item = GetDraggedItem(listBox);
        var container = GetDraggedContainer(listBox);
        if (item is null || container is null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(listBox);
        if (layer is null)
        {
            return;
        }

        SetOriginalIndex(listBox, listBox.Items.IndexOf(item));
        SetHasRestoredOriginalOrder(listBox, false);

        if (!listBox.IsMouseCaptured)
        {
            listBox.CaptureMouse();
        }

        SetIsDragInProgress(listBox, true);
        SetIsDragging(container, true);
        item.IsDragging = true;

        var adorner = new DragGhostAdorner(listBox, container, GetDragStartPoint(listBox));
        layer.Add(adorner);
        SetDragAdorner(listBox, adorner);

        var indicator = new DropIndicatorAdorner(listBox);
        layer.Add(indicator);
        SetDropIndicatorAdorner(listBox, indicator);

        SetLastReorderTarget(listBox, null);
        SetLastReorderInsertAfter(listBox, false);
        _activeListBox = listBox;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void EndDrag(ListBox listBox)
    {
        if (!GetIsDragInProgress(listBox))
        {
            SetDraggedItem(listBox, null);
            SetDraggedContainer(listBox, null);
            ClearDropTarget(listBox);
            return;
        }

        var point = GetCurrentPointerPoint(listBox);
        var isInside = point.X >= 0 && point.Y >= 0 && point.X <= listBox.ActualWidth && point.Y <= listBox.ActualHeight;

        CompositionTarget.Rendering -= OnRendering;
        if (ReferenceEquals(_activeListBox, listBox))
        {
            _activeListBox = null;
        }

        if (!isInside)
        {
            RestoreOriginalOrder(listBox);
        }

        var adorner = GetDragAdorner(listBox);
        if (adorner is not null)
        {
            var layer = AdornerLayer.GetAdornerLayer(listBox);
            adorner.BeginFadeOut(() =>
            {
                layer?.Remove(adorner);
            });
            SetDragAdorner(listBox, null);
        }

        var indicator = GetDropIndicatorAdorner(listBox);
        if (indicator is not null)
        {
            var layer = AdornerLayer.GetAdornerLayer(listBox);
            layer?.Remove(indicator);
            SetDropIndicatorAdorner(listBox, null);
        }

        var container = GetDraggedContainer(listBox);
        if (container is not null)
        {
            SetIsDragging(container, false);
        }
        var item = GetDraggedItem(listBox);
        if (item is not null)
        {
            item.IsDragging = false;
        }

        ClearDropTarget(listBox);
        SetDraggedItem(listBox, null);
        SetDraggedContainer(listBox, null);
        SetIsDragInProgress(listBox, false);

        if (listBox.IsMouseCaptured)
        {
            listBox.ReleaseMouseCapture();
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var listBox = _activeListBox;
        if (listBox is null || !GetIsDragInProgress(listBox))
        {
            return;
        }

        var adorner = GetDragAdorner(listBox);
        if (adorner is null)
        {
            return;
        }

        var point = GetCurrentPointerPoint(listBox);
        adorner.Update(point);
    }

    private static void UpdateReorder(ListBox listBox, Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > listBox.ActualWidth || point.Y > listBox.ActualHeight)
        {
            HideIndicator(listBox);

            if (!GetHasRestoredOriginalOrder(listBox))
            {
                RestoreOriginalOrder(listBox);
                SetHasRestoredOriginalOrder(listBox, true);
            }

            ClearDropTarget(listBox);
            return;
        }

        SetHasRestoredOriginalOrder(listBox, false);

        var source = GetDraggedItem(listBox);
        if (source is null)
        {
            return;
        }

        var insertion = ComputeInsertion(listBox, source, point);
        SetDropTarget(listBox, insertion.HighlightContainer);
        UpdateIndicator(listBox, insertion.IndicatorY);

        var target = insertion.Target;
        var insertAfter = insertion.InsertAfter;

        if (ReferenceEquals(GetLastReorderTarget(listBox), target) && GetLastReorderInsertAfter(listBox) == insertAfter)
        {
            return;
        }

        SetLastReorderTarget(listBox, target);
        SetLastReorderInsertAfter(listBox, insertAfter);

        var before = CaptureContainerPositions(listBox);

        var command = GetReorderCommand(listBox);
        var request = new ProcessReorderRequest(source, target, insertAfter);
        if (command is not null && command.CanExecute(request))
        {
            command.Execute(request);
        }

        AnimateContainersToNewPositions(listBox, before, source);
    }

    private static void SetDropTarget(ListBox listBox, ListBoxItem? newTarget)
    {
        var current = GetDropTargetContainer(listBox);
        if (ReferenceEquals(current, newTarget))
        {
            return;
        }

        if (current is not null)
        {
            SetIsDropTarget(current, false);
        }

        if (newTarget is not null)
        {
            SetIsDropTarget(newTarget, true);
        }

        SetDropTargetContainer(listBox, newTarget);
    }

    private static void ClearDropTarget(ListBox listBox)
    {
        var current = GetDropTargetContainer(listBox);
        if (current is not null)
        {
            SetIsDropTarget(current, false);
            SetDropTargetContainer(listBox, null);
        }
    }

    private static ProcessSelectionItem? GetItemUnderPointer(ListBox listBox, Point point)
    {
        var container = GetContainerUnderPointer(listBox, point);
        return container is null ? null : (ProcessSelectionItem?)listBox.ItemContainerGenerator.ItemFromContainer(container);
    }

    private static ListBoxItem? GetContainerUnderPointer(ItemsControl control, Point point)
    {
        var element = control.InputHitTest(point) as DependencyObject;
        while (element is not null)
        {
            if (element is ListBoxItem listBoxItem)
            {
                return listBoxItem;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static void RestoreOriginalOrder(ListBox listBox)
    {
        var dragged = GetDraggedItem(listBox);
        if (dragged is null)
        {
            return;
        }

        var originalIndex = GetOriginalIndex(listBox);
        if (originalIndex < 0)
        {
            return;
        }

        var currentIndex = listBox.Items.IndexOf(dragged);
        if (currentIndex < 0 || currentIndex == originalIndex)
        {
            return;
        }

        var before = CaptureContainerPositions(listBox);

        var command = GetReorderCommand(listBox);
        ProcessSelectionItem? target = null;
        bool insertAfter = false;

        if (originalIndex >= 0 && originalIndex < listBox.Items.Count)
        {
            var targetObj = listBox.Items[originalIndex] as ProcessSelectionItem;
            if (ReferenceEquals(targetObj, dragged))
            {
                if (originalIndex > 0)
                {
                    targetObj = listBox.Items[originalIndex - 1] as ProcessSelectionItem;
                    insertAfter = true;
                }
            }

            target = targetObj;
        }

        if (command is not null)
        {
            var request = new ProcessReorderRequest(dragged, target, insertAfter);
            if (command.CanExecute(request))
            {
                command.Execute(request);
            }
        }

        AnimateContainersToNewPositions(listBox, before, dragged);
    }

    private static void UpdateIndicator(ListBox listBox, double? y)
    {
        var indicator = GetDropIndicatorAdorner(listBox);
        if (indicator is null)
        {
            return;
        }

        if (y is null)
        {
            indicator.IsIndicatorVisible = false;
            return;
        }

        indicator.IsIndicatorVisible = true;
        indicator.UpdateY(y.Value);
    }

    private static void HideIndicator(ListBox listBox) => UpdateIndicator(listBox, null);

    private static Dictionary<ProcessSelectionItem, double> CaptureContainerPositions(ListBox listBox)
    {
        var positions = new Dictionary<ProcessSelectionItem, double>();
        foreach (var obj in listBox.Items)
        {
            if (obj is not ProcessSelectionItem item)
            {
                continue;
            }

            var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(item);
            if (container is null)
            {
                continue;
            }

            var topLeft = container.TranslatePoint(new Point(0, 0), listBox);
            positions[item] = topLeft.Y;
        }

        return positions;
    }

    private static void AnimateContainersToNewPositions(ListBox listBox, Dictionary<ProcessSelectionItem, double> before, ProcessSelectionItem draggedItem)
    {
        listBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var obj in listBox.Items)
            {
                if (obj is not ProcessSelectionItem item)
                {
                    continue;
                }

                if (ReferenceEquals(item, draggedItem) || item.IsDragging)
                {
                    continue;
                }

                if (!before.TryGetValue(item, out var oldY))
                {
                    continue;
                }

                var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(item);
                if (container is null)
                {
                    continue;
                }

                var newY = container.TranslatePoint(new Point(0, 0), listBox).Y;
                var delta = oldY - newY;
                if (Math.Abs(delta) < 0.5)
                {
                    continue;
                }

                TranslateTransform transform;
                if (container.RenderTransform is TranslateTransform t)
                {
                    transform = t;
                }
                else
                {
                    transform = new TranslateTransform();
                    container.RenderTransform = transform;
                }

                container.RenderTransformOrigin = new Point(0.5, 0.5);
                transform.Y = delta;

                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(240),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                transform.BeginAnimation(TranslateTransform.YProperty, animation, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static (ProcessSelectionItem? Target, bool InsertAfter, double? IndicatorY, ListBoxItem? HighlightContainer) ComputeInsertion(ListBox listBox, ProcessSelectionItem source, Point point)
    {
        var items = listBox.Items.OfType<ProcessSelectionItem>().ToList();
        if (items.Count <= 1)
        {
            return (null, true, null, null);
        }

        var others = items.Where(i => !ReferenceEquals(i, source)).ToList();
        if (others.Count == 0)
        {
            return (null, true, null, null);
        }

        var insertIndex = others.Count;
        for (int i = 0; i < others.Count; i++)
        {
            var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(others[i]);
            if (container is null)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), listBox).Y;
            var mid = top + (container.ActualHeight / 2);
            if (point.Y < mid)
            {
                insertIndex = i;
                break;
            }
        }

        ProcessSelectionItem? target;
        bool insertAfter;
        double? indicatorY;
        ListBoxItem? highlightContainer;

        if (insertIndex <= 0)
        {
            target = others[0];
            insertAfter = false;
            var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(target);
            indicatorY = container?.TranslatePoint(new Point(0, 0), listBox).Y ?? 0;
            highlightContainer = container;
        }
        else if (insertIndex >= others.Count)
        {
            target = others[^1];
            insertAfter = true;
            var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(target);
            indicatorY = container?.TranslatePoint(new Point(0, container.ActualHeight), listBox).Y;
            highlightContainer = container;
        }
        else
        {
            target = others[insertIndex];
            insertAfter = false;
            var container = (ListBoxItem?)listBox.ItemContainerGenerator.ContainerFromItem(target);
            indicatorY = container?.TranslatePoint(new Point(0, 0), listBox).Y;
            highlightContainer = container;
        }

        return (target, insertAfter, indicatorY, highlightContainer);
    }

    private sealed class DragGhostAdorner : Adorner
    {
        private readonly VisualBrush _brush;
        private readonly Point _startPoint;
        private Point _currentPoint;
        private readonly Size _size;
        private double _opacity = 1;

        public DragGhostAdorner(UIElement adornedElement, UIElement sourceVisual, Point startPoint)
            : base(adornedElement)
        {
            _brush = new VisualBrush(sourceVisual) { Opacity = 0.9, Stretch = Stretch.None };
            _startPoint = startPoint;
            _currentPoint = startPoint;
            _size = sourceVisual.RenderSize;
            IsHitTestVisible = false;
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black };
            RenderTransform = new ScaleTransform(1.03, 1.03);
        }

        public void Update(Point currentPoint)
        {
            _currentPoint = currentPoint;
            InvalidateVisual();
            InvalidateArrange();
        }

        public void BeginFadeOut(Action onCompleted)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            animation.Completed += (_, _) => onCompleted();
            BeginAnimation(OpacityProxyProperty, animation);
        }

        public static readonly DependencyProperty OpacityProxyProperty =
            DependencyProperty.Register(
                nameof(OpacityProxy),
                typeof(double),
                typeof(DragGhostAdorner),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnOpacityProxyChanged));

        public double OpacityProxy
        {
            get => (double)GetValue(OpacityProxyProperty);
            set => SetValue(OpacityProxyProperty, value);
        }

        private static void OnOpacityProxyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var adorner = (DragGhostAdorner)d;
            adorner._opacity = (double)e.NewValue;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushOpacity(_opacity);
            drawingContext.DrawRectangle(_brush, null, new Rect(new Point(0, 0), _size));
            drawingContext.Pop();
        }

        public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
        {
            var baseTransform = base.GetDesiredTransform(transform);
            var group = new GeneralTransformGroup();
            group.Children.Add(baseTransform);
            group.Children.Add(new TranslateTransform(_currentPoint.X - _startPoint.X, _currentPoint.Y - _startPoint.Y));
            return group;
        }
    }

    private sealed class DropIndicatorAdorner : Adorner
    {
        private double _y;
        public bool IsIndicatorVisible { get; set; }

        public DropIndicatorAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void UpdateY(double y)
        {
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!IsIndicatorVisible)
            {
                return;
            }

            var width = AdornedElement.RenderSize.Width;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 120, 212)), 2);
            drawingContext.DrawLine(pen, new Point(4, _y), new Point(Math.Max(4, width - 4), _y));
        }
    }
}

