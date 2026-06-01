using KlevaDeploy.Services.Interfaces;
using System.Windows;

namespace KlevaDeploy.Views;

public partial class UnrarRequiredDialog : Window
{
    public IDialogService.UnrarPromptResult Result { get; private set; } = IDialogService.UnrarPromptResult.FermaCoda;

    public UnrarRequiredDialog(string processName, string details)
    {
        InitializeComponent();

        BodyText.Text =
            $"Per completare \"{processName}\", serve UnRAR per estrarre l'installer.\n\n" +
            "Puoi installarlo ora oppure fermare la coda.\n" +
            "Saltare questo passaggio non è raccomandato: alcune funzionalità chiave non saranno disponibili.";

        DetailsText.Text = details ?? string.Empty;

        InstallButton.Click += (_, _) =>
        {
            Result = IDialogService.UnrarPromptResult.Installa;
            DialogResult = true;
        };

        StopQueueButton.Click += (_, _) =>
        {
            Result = IDialogService.UnrarPromptResult.FermaCoda;
            DialogResult = true;
        };

        SkipButton.Click += (_, _) =>
        {
            var confirmed = MessageBox.Show(
                this,
                "Sei sicuro di voler saltare questo passaggio?\n\nNon è raccomandato: alcune funzionalità chiave non saranno disponibili.",
                "Conferma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

            if (!confirmed) return;

            Result = IDialogService.UnrarPromptResult.SaltaPassaggio;
            DialogResult = true;
        };

        BtnClose.Click += (_, _) =>
        {
            Result = IDialogService.UnrarPromptResult.FermaCoda;
            DialogResult = false;
        };

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }
}
