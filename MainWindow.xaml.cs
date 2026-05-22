using System.Windows;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // ── Title bar chrome wiring (window decoration only — not business logic) ──

        // Single handler: drag on single-click, maximize/restore on double-click
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
                ToggleMaximize();
            else
                DragMove();
        };

        // Minimize button
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;

        // Maximize / Restore button
        BtnMaximize.Click += (_, _) => ToggleMaximize();

        // Close button
        BtnClose.Click += (_, _) => Close();

        // Keep the maximize icon glyph in sync when state changes externally
        StateChanged += (_, _) =>
        {
            MaximizeIcon.Text = WindowState == WindowState.Maximized
                ? "\u2752"   // ❒ restore glyph
                : "\u25A1";  // □ maximize glyph
        };
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
