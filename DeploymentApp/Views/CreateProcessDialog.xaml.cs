using System.Windows;
using DeploymentApp.ViewModels;

namespace DeploymentApp.Views;

public partial class CreateProcessDialog : Window
{
    private readonly CreateProcessViewModel _viewModel;

    public CreateProcessDialog(CreateProcessViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Monitor property changes to close dialog when commands complete
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateProcessViewModel.DialogResult))
            {
                DialogResult = _viewModel.DialogResult;
                Close();
            }
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
