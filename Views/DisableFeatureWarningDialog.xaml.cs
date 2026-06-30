using System.Windows;

namespace KlevaDeploy.Views;

public partial class DisableFeatureWarningDialog : Window
{
    public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

    public DisableFeatureWarningDialog(string featureName)
    {
        InitializeComponent();

        BodyText.Text =
            $"La funzionalità \"{featureName}\" è richiesta dal pacchetto selezionato.\n" +
            "Disattivarla potrebbe causare un'installazione incompleta o non funzionante.\n\n" +
            "Sei sicuro di voler continuare?";

        ConfirmButton.Click += (_, _) => { DialogResult = true; };
        CancelButton.Click  += (_, _) => { DialogResult = false; };
        BtnClose.Click += (_, _) => { DialogResult = false; };
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }
}
