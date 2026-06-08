using System.Windows;
using System.Windows.Controls;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class ArgumentPromptDialog : Window
{
    private readonly ArgumentPromptViewModel _vm;

    public ArgumentPromptDialog(ArgumentPromptViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        BtnClose.Click += (_, _) => { DialogResult = false; };
        RunOnceButton.Click += (_, _) =>
        {
            _vm.Choose(ArgumentPromptMode.RunOnce);
            DialogResult = true;
        };
        RunAlwaysButton.Click += (_, _) =>
        {
            _vm.Choose(ArgumentPromptMode.RunAlways);
            DialogResult = true;
        };

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }

    private void OnPasswordLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;
        if (pb.DataContext is not ArgumentPromptItemViewModel item) return;
        if (pb.Password != item.Value)
            pb.Password = item.Value ?? string.Empty;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;
        if (pb.DataContext is not ArgumentPromptItemViewModel item) return;
        item.Value = pb.Password;
    }
}

