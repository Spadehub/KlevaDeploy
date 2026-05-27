using System.Windows;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.CloseRequested += (_, _) => DialogResult = true;

        Closing += (_, e) =>
        {
            if (!vm.CanClose())
            {
                e.Cancel = true;
            }
        };

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }
}
