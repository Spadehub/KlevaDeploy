using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class CreateProcessDialog : Window
{
    private readonly CreateProcessViewModel _viewModel;

    public ScrollViewer LayoutContentScrollViewer => ContentScrollViewer;

    public CreateProcessDialog(CreateProcessViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) => BeginSlideInAnimation();

        // Monitor property changes to close dialog when commands complete
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateProcessViewModel.DialogResult))
            {
                if (_viewModel.DialogResult is null)
                {
                    return;
                }

                DialogResult = _viewModel.DialogResult;
                Close();
            }
        };
    }

    private void BeginSlideInAnimation()
    {
        var finalLeft = Left;
        var offset = ActualWidth > 0 ? ActualWidth : Width;

        Left = finalLeft + offset;
        Opacity = 0;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slide = new DoubleAnimation
        {
            To = finalLeft,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = easeOut
        };

        var fade = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromSeconds(0.2),
            EasingFunction = easeOut
        };

        BeginAnimation(LeftProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
