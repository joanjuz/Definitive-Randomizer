using System;
using System.Windows;
using System.Windows.Input;

namespace ThreeDS.Progressive;

/// <summary>Diálogos accesibles y estilizados para todos los módulos de Studio.</summary>
public partial class StudioDialog : Window
{
    public StudioDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ConfirmButton.Focus();
    }

    public static bool Confirm(Window? owner, string title, string message, string? details = null)
    {
        var dlg = Create(owner, title, message, details, "!", "Continuar", true);
        return dlg.ShowDialog() == true;
    }

    public static void Notice(Window? owner, string title, string message, string? details = null)
    {
        var dlg = Create(owner, title, message, details, "i", "Entendido", false);
        dlg.ShowDialog();
    }

    public static void Error(Window? owner, string title, string message)
    {
        var dlg = Create(owner, title, message, null, "!", "Entendido", false);
        dlg.ShowDialog();
    }

    private static StudioDialog Create(Window? owner, string title, string message, string? details,
        string symbol, string action, bool canCancel)
    {
        var dialog = new StudioDialog();
        if (owner != null)
            dialog.Owner = owner;
        dialog.TitleLabel.Text = title;
        dialog.Title = title;
        dialog.MessageLabel.Text = message;
        dialog.SymbolLabel.Text = symbol;
        dialog.DetailsLabel.Text = details ?? string.Empty;
        dialog.DetailsLabel.Visibility = string.IsNullOrWhiteSpace(details)
            ? Visibility.Collapsed : Visibility.Visible;
        dialog.ConfirmButton.Content = action;
        dialog.CancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        return dialog;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}