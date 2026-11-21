using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrelloNotifier.Models;
using TrelloNotifier.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TrelloNotifier;

public sealed partial class DiagnosticsPage : Page
{
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _loadCancellation;
    private DiagnosticLogSnapshot _snapshot = new();
    private bool _updatingFiles;
    private bool _loading;

    public DiagnosticsPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation = new CancellationTokenSource();
        await LoadLogsAsync(refreshFiles: true);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadLogsAsync(refreshFiles: true);

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DiagnosticLogEntry entry }) return;
        try
        {
            var content = new DataPackage();
            content.SetText(entry.RawLine);
            Clipboard.SetContent(content);
            Clipboard.Flush();
            ShowMessage("Línea de log copiada al portapapeles.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            AppLog.WriteFailure("CopyDiagnosticLog", ex);
            ShowMessage("No se pudo copiar la línea. Vuelve a intentarlo.", InfoBarSeverity.Error);
        }
    }

    private async void File_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFiles) await LoadLogsAsync(refreshFiles: false);
    }

    private void Level_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogsList is not null) ApplyFilter();
    }

    private async Task LoadLogsAsync(bool refreshFiles)
    {
        if (_pageCancellation is null) return;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        CancellationToken token = _loadCancellation.Token;
        _loading = true;
        RefreshButton.IsEnabled = false;
        FileComboBox.IsEnabled = false;
        LoadingProgress.Visibility = Visibility.Visible;
        DiagnosticInfoBar.IsOpen = false;
        UpdatedText.Text = "Cargando registros...";
        _snapshot = new DiagnosticLogSnapshot();
        ApplyFilter();
        try
        {
            if (refreshFiles)
            {
                string? selected = FileComboBox.SelectedItem as string;
                IReadOnlyList<string> files = await AppServices.Logs.GetFilesAsync(token);
                token.ThrowIfCancellationRequested();
                _updatingFiles = true;
                try
                {
                    FileComboBox.ItemsSource = files;
                    FileComboBox.SelectedItem = selected is not null && files.Contains(selected) ? selected : files.FirstOrDefault();
                }
                finally
                {
                    _updatingFiles = false;
                }
            }
            if (FileComboBox.SelectedItem is string fileName)
            {
                DiagnosticLogSnapshot snapshot = await AppServices.Logs.ReadRecentAsync(fileName, token);
                token.ThrowIfCancellationRequested();
                _snapshot = snapshot;
            }
            token.ThrowIfCancellationRequested();
            UpdatedText.Text = $"Actualizado a las {DateTime.Now:HH:mm:ss}. Pulsa Actualizar para ver nuevos registros.";
            if (AppLog.HasWriteFailure)
                ShowMessage("La aplicación no pudo escribir todos los logs. Revisa los permisos y el espacio disponible.", InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Una carga nueva o la navegación descarta los resultados anteriores.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!token.IsCancellationRequested)
            {
                AppLog.WriteFailure("ReadDiagnosticLogs", ex);
                UpdatedText.Text = "La lectura no se completó. Pulsa Actualizar para reintentar.";
                ShowMessage(ex is FileNotFoundException or DirectoryNotFoundException
                    ? "El archivo ya no está disponible; puede haber rotado. Pulsa Actualizar para renovar la lista."
                    : "No se pudieron leer los logs. Revisa los permisos de acceso y vuelve a intentarlo.", InfoBarSeverity.Error);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _loading = false;
                RefreshButton.IsEnabled = true;
                FileComboBox.IsEnabled = FileComboBox.Items.Count > 0;
                LoadingProgress.Visibility = Visibility.Collapsed;
                ApplyFilter();
            }
        }
    }

    private void ApplyFilter()
    {
        string level = (LevelComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        List<DiagnosticLogEntry> entries = _snapshot.Entries.Where(entry => level == "All" || entry.Severity == level).ToList();
        LogsList.ItemsSource = entries;
        EmptyText.Visibility = !_loading && entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _snapshot.Entries.Count > 0 ? "No hay registros del nivel seleccionado."
            : FileComboBox.Items.Count == 0 ? "Todavía no hay logs disponibles."
            : "El archivo no contiene registros completos para mostrar.";
        SummaryText.Text = $"{entries.Count} de {_snapshot.Entries.Count} registros cargados";
        LimitText.Visibility = _snapshot.IsTruncated ? Visibility.Visible : Visibility.Collapsed;
        LimitText.Text = $"Vista limitada a las últimas {AppLogReader.MaxEntries} líneas completas de los últimos 2 MiB del archivo.";
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        DiagnosticInfoBar.Message = message;
        DiagnosticInfoBar.Severity = severity;
        DiagnosticInfoBar.IsOpen = true;
    }

    private void LogRow_Loaded(object sender, RoutedEventArgs e) => ApplyLogState((Control)sender);
    private void LogRow_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e) => ApplyLogState((Control)sender);

    private static void ApplyLogState(Control control)
    {
        if (control.DataContext is DiagnosticLogEntry entry)
            VisualStateManager.GoToState(control, entry.Severity, false);
    }
}
