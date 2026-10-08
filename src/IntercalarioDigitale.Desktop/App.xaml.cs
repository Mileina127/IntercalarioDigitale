using System.Windows;
using System.Windows.Threading;

namespace IntercalarioDigitale.Desktop;

public partial class App : Application
{
    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Si è verificato un errore imprevisto:\n\n" + e.Exception.Message +
            "\n\nI dati sono in " + AppPaths.DataFolder,
            "GerarchIA",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;

        // Errore all'avvio: senza finestre l'app resterebbe in esecuzione senza interfaccia.
        if (Windows.Count == 0)
        {
            Shutdown();
        }
    }
}
