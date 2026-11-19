using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TrelloNotifier.Models;
using TrelloNotifier.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace TrelloNotifier;

public sealed partial class BulkCreateCardPage : Page
{
    private readonly ObservableCollection<CardImportRow> _rows = new();
    private List<CardImportRow> _sourceRows = new();
    private List<TrelloMember> _members = new();
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _boardCancellation;
    private AppSettings? _settings;
    private bool _loadingBoards;
    private bool _loadingMembers;
    private bool _membersLoaded;
    private bool _fileBusy;
    private bool _savingFile;
    private bool _reviewing;
    private bool _started;
    private bool _running;
    private bool _stopRequested;
    private bool _generating;
    private bool _editing;
    private bool _sourceIsAi;
    private CancellationTokenSource? _geminiCancellation;
    private ContentDialog? _editDialog;

    public BulkCreateCardPage()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation = new CancellationTokenSource();
        await LoadBoardsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _geminiCancellation?.Cancel();
        _editDialog?.Hide();
        _boardCancellation?.Cancel();
        _boardCancellation?.Dispose();
        _boardCancellation = null;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (_running)
        {
            e.Cancel = true;
            ShowMessage("Detén la importación y espera a que termine la tarjeta en curso antes de salir.", InfoBarSeverity.Informational);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await LoadBoardsAsync();

    private async Task LoadBoardsAsync()
    {
        if (_pageCancellation is null || _loadingBoards || _started || _generating || _editing) return;
        CancellationToken token = _pageCancellation.Token;
        _loadingBoards = true;
        _boardCancellation?.Cancel();
        _boardCancellation?.Dispose();
        _boardCancellation = null;
        _loadingMembers = false;
        _membersLoaded = false;
        BoardComboBox.ItemsSource = null;
        SetLoading(true, "Cargando tableros...");
        UpdateActions();
        try
        {
            _settings = await Task.Run(AppServices.Settings.Load, token);
            token.ThrowIfCancellationRequested();
            if (!_settings.IsConfigured)
            {
                ShowMessage("Configura la conexión con Trello antes de importar tarjetas.", InfoBarSeverity.Warning);
                return;
            }
            List<TrelloBoard> boards = await AppServices.Trello.GetCreationBoardsAsync(_settings, token);
            token.ThrowIfCancellationRequested();
            BoardComboBox.ItemsSource = boards.Where(board => board is not null && !string.IsNullOrWhiteSpace(board.Id))
                .OrderBy(board => board.Name).ToList();
            if (BoardComboBox.Items.Count > 0) BoardComboBox.SelectedIndex = 0;
            else ShowMessage("No hay tableros abiertos disponibles.", InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // La navegación cancela la consulta sin publicar sus resultados.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                AppLog.WriteFailure("LoadBulkCreationBoards", ex);
                ShowMessage("No se pudieron cargar los tableros. Revisa la conexión y pulsa Volver a cargar tableros.", InfoBarSeverity.Error);
            }
        }
        finally
        {
            _loadingBoards = false;
            if (!token.IsCancellationRequested)
            {
                if (!_loadingMembers) SetLoading(false);
                UpdateActions();
            }
        }
    }

    private async void Board_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _boardCancellation?.Cancel();
        _boardCancellation?.Dispose();
        _boardCancellation = null;
        _membersLoaded = false;
        _loadingMembers = false;
        _members.Clear();
        ListComboBox.ItemsSource = null;
        MembersList.ItemsSource = null;
        UpdateActions();
        if (_pageCancellation is null || _settings is null || BoardComboBox.SelectedItem is not TrelloBoard board) return;
        _boardCancellation = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        CancellationToken token = _boardCancellation.Token;
        _loadingMembers = true;
        SetLoading(true, "Cargando responsables del tablero...");
        UpdateActions();
        try
        {
            List<TrelloMember> members = await AppServices.Trello.GetBoardMembersAsync(_settings, board.Id, token);
            token.ThrowIfCancellationRequested();
            _members = members.Where(member => member is not null && !string.IsNullOrWhiteSpace(member.Id))
                .OrderBy(member => member.DisplayName).ToList();
            MembersList.ItemsSource = _members;
            ListComboBox.ItemsSource = (board.Lists ?? new List<TrelloList>()).Where(list => list is not null && !string.IsNullOrWhiteSpace(list.Id)).ToList();
            _membersLoaded = true;
            if (ListComboBox.Items.Count > 0) ListComboBox.SelectedIndex = 0;
            else ShowMessage("El tablero no tiene listas abiertas. Selecciona otro tablero o crea una lista en Trello.", InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Un nuevo tablero descarta la respuesta del anterior.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                AppLog.WriteFailure("LoadBulkCreationMembers", ex);
                ShowMessage("No se pudieron cargar los responsables. Vuelve a cargar los tableros o selecciona otro.", InfoBarSeverity.Error);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _loadingMembers = false;
                SetLoading(false);
                UpdateActions();
            }
        }
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e) => await ChooseFileAsync();

    private bool CanReadCsv => _pageCancellation is not null && !_generating && !_editing && !_fileBusy && !_savingFile && !_loadingBoards && !_started && !_reviewing;

    private async Task ChooseFileAsync()
    {
        if (!CanReadCsv) return;
        CancellationToken token = _pageCancellation!.Token;
        _fileBusy = true;
        UpdateActions();
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".csv");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).WindowHandle);
            StorageFile? file = await picker.PickSingleFileAsync();
            token.ThrowIfCancellationRequested();
            if (file is null) return;
            await ReadCsvFileAsync(file, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // La vista ya no recibe el contenido del archivo.
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or COMException)
        {
            if (!token.IsCancellationRequested) ShowCsvReadError(ex);
        }
        finally
        {
            _fileBusy = false;
            if (!token.IsCancellationRequested) UpdateActions();
        }
    }

    private void Csv_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = CanReadCsv && e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void Csv_Drop(object sender, DragEventArgs e)
    {
        if (!CanReadCsv || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        CancellationToken token = _pageCancellation!.Token;
        var deferral = e.GetDeferral();
        e.Handled = true;
        _fileBusy = true;
        UpdateActions();
        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            token.ThrowIfCancellationRequested();
            if (items.Count != 1 || items[0] is not StorageFile file || !file.FileType.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Arrastra un único archivo CSV.");
            await ReadCsvFileAsync(file, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // La navegación descarta el archivo arrastrado.
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or COMException)
        {
            if (!token.IsCancellationRequested) ShowCsvReadError(ex);
        }
        finally
        {
            deferral.Complete();
            _fileBusy = false;
            if (!token.IsCancellationRequested) UpdateActions();
        }
    }

    private async Task ReadCsvFileAsync(StorageFile file, CancellationToken token)
    {
        using IRandomAccessStreamWithContentType stream = await file.OpenReadAsync();
        if (stream.Size > CardCsvImporter.MaxFileBytes) throw new FormatException("El CSV no puede superar 2 MiB.");
        uint size = (uint)stream.Size;
        using var reader = new DataReader(stream) { UnicodeEncoding = UnicodeEncoding.Utf8 };
        uint read = await reader.LoadAsync(size);
        token.ThrowIfCancellationRequested();
        if (read != size) throw new IOException("No se pudo leer el archivo completo.");
        string text = reader.ReadString(read);
        List<CardImportRow> rows = await Task.Run(() => CardCsvImporter.Parse(text, token), token);
        token.ThrowIfCancellationRequested();
        _sourceRows = rows;
        _sourceIsAi = false;
        SelectedFileName.Text = file.Name;
        ReviewFileName.Text = file.Name;
        FileSummary.Text = $"{rows.Count} filas detectadas · {Math.Max(1, (size + 1023) / 1024)} KiB";
        ImportInfoBar.IsOpen = false;
    }

    private void ShowCsvReadError(Exception error)
    {
        if (error is FormatException)
            ShowMessage(error.Message, InfoBarSeverity.Warning);
        else
        {
            AppLog.WriteFailure("ReadCardImportCsv", error);
            ShowMessage("No se pudo leer el CSV. Comprueba los permisos y guárdalo como CSV UTF-8.", InfoBarSeverity.Error);
        }
    }

    private void Members_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedMembersText is null) return;
        string[] selected = MembersList.SelectedItems.OfType<TrelloMember>().Select(member => member.DisplayName).ToArray();
        SelectedMembersText.Text = selected.Length == 0 ? "Sin asignar" : string.Join(", ", selected);
        ToolTipService.SetToolTip(MembersButton, SelectedMembersText.Text);
    }

    private void Destination_Changed(object sender, SelectionChangedEventArgs e) => UpdateActions();
    private void ExcludeInvalid_Changed(object sender, RoutedEventArgs e) => UpdateActions();

    private void Source_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeminiInputPanel is null || CsvInputPanel is null) return;
        bool ai = SourceComboBox.SelectedIndex == 1;
        GeminiInputPanel.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
        CsvInputPanel.Visibility = ai ? Visibility.Collapsed : Visibility.Visible;
        CsvFieldsPanel.Visibility = ai ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CancelGemini_Click(object sender, RoutedEventArgs e) => _geminiCancellation?.Cancel();

    private async void GenerateGemini_Click(object sender, RoutedEventArgs e)
    {
        if (_pageCancellation is null || _generating || _started || _reviewing || !GenerateGeminiButton.IsEnabled || !SetupPanel.IsEnabled) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        _geminiCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        _generating = true;
        SetLoading(true, "Generando propuestas con Gemini...");
        UpdateActions();
        bool generated = false;
        try
        {
            AppSettings settings = await Task.Run(AppServices.Settings.Load, token);
            List<CardImportRow> rows = await AppServices.Gemini.GenerateAsync(settings, GeminiPromptTextBox.Text,
                false, DateTimeOffset.Now, TimeZoneInfo.Local, token);
            token.ThrowIfCancellationRequested();
            _sourceRows = rows;
            _sourceIsAi = true;
            SelectedFileName.Text = ReviewFileName.Text = "Propuestas de Gemini";
            FileSummary.Text = $"{rows.Count} propuestas · Revisa contenido y fechas antes de crear";
            ShowMessage("Propuestas generadas. Revisa las fechas y selecciona los responsables en la app.", InfoBarSeverity.Informational);
            generated = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // El origen anterior permanece disponible si se cancela la generación.
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            if (_pageCancellation?.IsCancellationRequested == false)
            {
                AppLog.WriteFailure("GenerateBulkGeminiCards", ex);
                ShowMessage(GeminiCardClient.GetErrorMessage(ex), InfoBarSeverity.Warning);
            }
        }
        finally
        {
            _geminiCancellation = null;
            _generating = false;
            if (_pageCancellation?.IsCancellationRequested == false)
            {
                SetLoading(false);
                UpdateActions();
                if (generated && ReviewButton.IsEnabled) Review_Click(sender, e);
            }
        }
    }

    private void RevalidateRows()
    {
        string listId = (ListComboBox.SelectedItem as TrelloList)?.Id ?? string.Empty;
        string[] defaults = MembersList.SelectedItems.OfType<TrelloMember>().Select(member => member.Id).ToArray();
        List<CardImportRow> validated = CardBatchPreparer.Prepare(_rows, listId, _members, defaults);
        for (int index = 0; index < validated.Count; index++) _rows[index] = validated[index];
    }

    private void ExcludeRow_Click(object sender, RoutedEventArgs e)
    {
        if (_started || _editing || sender is not HyperlinkButton { Tag: CardImportRow row }) return;
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        CardImportState state = row.State == CardImportState.Excluded ? CardImportState.Ready : CardImportState.Excluded;
        _rows[index] = row with { State = state };
        int sourceIndex = _sourceRows.FindIndex(source => source.RowNumber == row.RowNumber);
        _sourceRows[sourceIndex] = _sourceRows[sourceIndex] with { State = state };
        RevalidateRows();
        UpdateActions();
    }

    private async void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (_started || _editing || _pageCancellation is null || sender is not HyperlinkButton { Tag: CardImportRow row }) return;
        CancellationToken token = _pageCancellation.Token;
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        var title = new TextBox { Header = "Título", Text = row.Draft.Name, TextWrapping = TextWrapping.Wrap };
        var description = new TextBox { Header = "Descripción", Text = row.Draft.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 180 };
        var due = new TextBox { Header = "Vencimiento (ISO 8601 con zona, vacío = sin fecha)", Text = row.Draft.Due?.ToString("O") ?? "", PlaceholderText = "2026-12-05T09:00:00-05:00", MaxLength = 40 };
        var members = new TextBox { Header = "Usuarios separados por ; (vacío = predeterminados)", Text = row.RequestedMembers };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(new TextBlock { Text = "Revisa todos los campos. Guardar sustituye los datos de esta fila y vuelve a validarlos. " + row.ReviewNote, TextWrapping = TextWrapping.Wrap });
        foreach (UIElement element in new UIElement[] { title, description, due, members, error }) fields.Children.Add(element);
        _editDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = $"Editar tarjeta {row.RowNumber}",
            Content = new ScrollViewer { Content = fields, MaxHeight = 420 },
            PrimaryButtonText = "Guardar cambios",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        CardImportRow? edited = null;
        _editDialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var candidate = row with
                {
                    Draft = new TrelloCardDraft { Name = title.Text.Trim(), Description = description.Text, Due = CardBatchPreparer.ParseDue(due.Text) },
                    RequestedMembers = members.Text.Trim(),
                    ParseError = null,
                    Message = string.Empty,
                    ReviewNote = string.Empty,
                    State = CardImportState.Ready
                };
                string[] defaults = MembersList.SelectedItems.OfType<TrelloMember>().Select(member => member.Id).ToArray();
                CardImportRow validated = CardBatchPreparer.Prepare(new[] { candidate }, (ListComboBox.SelectedItem as TrelloList)?.Id ?? "", _members, defaults)[0];
                if (validated.State == CardImportState.Invalid) throw new FormatException(validated.Message);
                edited = validated with { State = row.State == CardImportState.Excluded ? CardImportState.Excluded : CardImportState.Ready };
            }
            catch (FormatException ex)
            {
                error.Text = ex.Message;
                args.Cancel = true;
            }
        };
        _editing = true;
        UpdateActions();
        try
        {
            if (await _editDialog.ShowAsync() != ContentDialogResult.Primary || token.IsCancellationRequested || edited is null) return;
            _rows[index] = edited;
            int sourceIndex = _sourceRows.FindIndex(source => source.RowNumber == row.RowNumber);
            _sourceRows[sourceIndex] = edited;
        }
        finally
        {
            _editing = false;
            _editDialog = null;
            if (!token.IsCancellationRequested) UpdateActions();
        }
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (!ReviewButton.IsEnabled || ListComboBox.SelectedItem is not TrelloList list) return;
        string[] defaults = MembersList.SelectedItems.OfType<TrelloMember>().Select(member => member.Id).ToArray();
        List<CardImportRow> prepared = CardBatchPreparer.Prepare(_sourceRows, list.Id, _members, defaults);
        _rows.Clear();
        foreach (CardImportRow row in prepared) _rows.Add(row);
        _reviewing = true;
        ExcludeInvalidCheckBox.IsChecked = false;
        DestinationText.Text = $"{(BoardComboBox.SelectedItem as TrelloBoard)?.Name} / {list.Name} · Fechas mostradas en hora local de Windows";
        ImportInfoBar.IsOpen = false;
        UpdateActions();
    }

    private void EditSource_Click(object sender, RoutedEventArgs e)
    {
        if (_started || _editing) return;
        _reviewing = false;
        UpdateActions();
    }

    private async void CorrectFile_Click(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _reviewing = false;
        UpdateActions();
        await ChooseFileAsync();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _editing || _generating || !CreateButton.IsEnabled || _settings is null || _pageCancellation is null) return;
        CancellationToken token = _pageCancellation.Token;
        if (!_started)
        {
            RevalidateRows();
            if (_rows.Any(row => row.State == CardImportState.Invalid) && ExcludeInvalidCheckBox.IsChecked != true)
            {
                UpdateActions();
                return;
            }
            for (int index = 0; index < _rows.Count; index++)
                if (_rows[index].State == CardImportState.Invalid)
                    _rows[index] = _rows[index] with { State = CardImportState.Excluded };
        }
        _started = true;
        _running = true;
        _stopRequested = false;
        ImportInfoBar.IsOpen = false;
        UpdateActions();
        try
        {
            for (int index = 0; index < _rows.Count && !_stopRequested; index++)
            {
                token.ThrowIfCancellationRequested();
                CardImportRow row = _rows[index];
                if (!row.CanSubmit) continue;
                _rows[index] = row with { State = CardImportState.Creating, Message = string.Empty };
                ProgressText.Text = $"Creando fila {row.RowNumber} de {_rows.Count}...";
                try
                {
                    TrelloCard card = await AppServices.Trello.CreateCardAsync(_settings, row.Draft, token);
                    _rows[index] = row with { State = CardImportState.Created, CreatedCard = card, Message = "Creada correctamente." };
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
                {
                    AppLog.WriteFailure("CreateBulkCard", ex);
                    bool rejected = TrelloApiClient.IsDefiniteCreationRejection(ex);
                    _rows[index] = row with
                    {
                        State = rejected ? CardImportState.Rejected : CardImportState.Uncertain,
                        Message = rejected
                            ? "Trello rechazó la solicitud. Revisa permisos y destino; si alcanzaste el límite de solicitudes, espera antes de reintentar."
                            : "Revisa el tablero antes de crear esta tarjeta de nuevo. Esta fila no se reenviará desde este lote."
                    };
                    ShowMessage("La importación se detuvo por un error. Revisa el resultado por fila; las tarjetas ya creadas no se reenviarán.", InfoBarSeverity.Warning);
                    _stopRequested = true;
                }
                UpdateSummary();
                // Envíos secuenciales y espaciados: no saturar Trello ni reintentar POST automáticamente.
                if (!_stopRequested && _rows.Skip(index + 1).Any(candidate => candidate.CanSubmit))
                    await Task.Delay(TimeSpan.FromMilliseconds(500), token);
            }
            if (!ImportInfoBar.IsOpen)
            {
                bool needsReview = _rows.Any(row => row.State is CardImportState.Rejected or CardImportState.Uncertain);
                ShowMessage(_stopRequested ? "Importación detenida. Puedes continuar con las filas pendientes."
                    : needsReview ? "El envío terminó con resultados que debes revisar. Consulta o descarga el detalle."
                    : "Importación finalizada. Consulta o descarga los resultados.",
                    needsReview ? InfoBarSeverity.Warning : _stopRequested ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelación entre solicitudes: las filas no enviadas siguen pendientes.
        }
        finally
        {
            _running = false;
            if (!token.IsCancellationRequested)
            {
                ProgressText.Text = "Los resultados se conservan mientras permanezcas en esta vista. Descárgalos antes de salir.";
                UpdateActions();
            }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        StopButton.IsEnabled = false;
        ProgressText.Text = "Se detendrá cuando termine la tarjeta en curso...";
    }

    private async void DownloadTemplate_Click(object sender, RoutedEventArgs e) => await SaveCsvAsync("plantilla-tarjetas", CardCsvImporter.Template);
    private async void Export_Click(object sender, RoutedEventArgs e) => await SaveCsvAsync("resultado-importacion", CardCsvImporter.ExportResults(_rows));

    private async Task SaveCsvAsync(string name, string text)
    {
        if (_savingFile || _running || _pageCancellation is null) return;
        CancellationToken token = _pageCancellation.Token;
        _savingFile = true;
        UpdateActions();
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = name };
            picker.FileTypeChoices.Add("CSV UTF-8", new List<string> { ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ((App)Application.Current).WindowHandle);
            StorageFile? file = await picker.PickSaveFileAsync();
            token.ThrowIfCancellationRequested();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, text, UnicodeEncoding.Utf8);
            if (!token.IsCancellationRequested) ShowMessage("Archivo CSV guardado.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // La navegación descarta el resultado del selector de archivos.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
        {
            if (!token.IsCancellationRequested)
            {
                AppLog.WriteFailure("SaveCardImportCsv", ex);
                ShowMessage("No se pudo guardar el CSV. Revisa los permisos y el espacio disponible.", InfoBarSeverity.Error);
            }
        }
        finally
        {
            _savingFile = false;
            if (!token.IsCancellationRequested) UpdateActions();
        }
    }

    private void OpenCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { Tag: CardImportRow row } || row.CreatedCard?.TrelloUri is not Uri uri) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.WriteFailure("OpenImportedCard", ex);
            ShowMessage("No se pudo abrir el navegador. Puedes usar el enlace del CSV de resultados.", InfoBarSeverity.Warning);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => MainPage.Current?.NavigateToCards(TrelloCardScope.Pending);

    private void NewImport_Click(object sender, RoutedEventArgs e)
    {
        if (!_running && !_savingFile) MainPage.Current?.NavigateToBulkCreation();
    }

    private void ImportRow_Loaded(object sender, RoutedEventArgs e) => ApplyRowVisuals((Control)sender);
    private void ImportRow_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e) => ApplyRowVisuals((Control)sender);

    private void ApplyRowVisuals(Control control)
    {
        if (control.DataContext is not CardImportRow row) return;
        VisualStateManager.GoToState(control, row.State.ToString(), false);
        VisualStateManager.GoToState(control, _started ? "ResultRow" : "PreviewRow", false);
        if (control.FindName("ReviewRowActions") is StackPanel actions)
            actions.Visibility = _started ? Visibility.Collapsed : Visibility.Visible;
        if (control.FindName("ExcludeRowButton") is HyperlinkButton exclude)
            exclude.Content = row.State == CardImportState.Excluded ? "Incluir" : "Excluir";
        if (control.FindName("RowMessage") is TextBlock message)
            message.Visibility = row.State is CardImportState.Invalid or CardImportState.Rejected or CardImportState.Uncertain or CardImportState.Excluded
                ? Visibility.Visible : Visibility.Collapsed;
        if (control.FindName("BadgeText") is TextBlock badge)
        {
            badge.Text = row.State switch
            {
                CardImportState.Ready => _started ? "Pendiente" : "✓ Válida",
                CardImportState.Created => "✓ Creada",
                CardImportState.Rejected => "No creada",
                CardImportState.Uncertain => "Por revisar",
                _ => row.StatusText
            };
        }
    }

    private void UpdateActions()
    {
        // SelectionChanged puede ejecutarse durante InitializeComponent.
        if (ReviewButton is null) return;
        bool busy = _loadingBoards || _loadingMembers || _fileBusy || _savingFile || _generating || _editing;
        SetupPanel.IsEnabled = !_started && !_loadingBoards && !_fileBusy && !_savingFile && !_generating && !_editing;
        GenerateGeminiButton.IsEnabled = !busy && !_started && !_reviewing;
        CancelGeminiButton.Visibility = _generating ? Visibility.Visible : Visibility.Collapsed;
        SetupPanel.Visibility = _reviewing ? Visibility.Collapsed : Visibility.Visible;
        ReviewPanel.Visibility = _reviewing ? Visibility.Visible : Visibility.Collapsed;
        ReviewButton.Visibility = _reviewing ? Visibility.Collapsed : Visibility.Visible;
        ReviewButton.IsEnabled = !_started && !busy && _sourceRows.Count > 0 && _membersLoaded && ListComboBox.SelectedItem is TrelloList;
        ReviewButton.Content = _sourceRows.Count == 0 ? "Revisar tarjetas →" : $"Revisar {_sourceRows.Count} tarjetas →";
        SelectedFilePanel.Visibility = _sourceRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.IsEnabled = !_loadingBoards;
        MembersButton.IsEnabled = _membersLoaded && !_loadingMembers;
        EditSourceButton.Visibility = _reviewing && !_started ? Visibility.Visible : Visibility.Collapsed;
        ExcludeInvalidCheckBox.Visibility = _reviewing && !_started && _rows.Any(row => row.State == CardImportState.Invalid) ? Visibility.Visible : Visibility.Collapsed;
        int eligible = _rows.Count(row => row.CanSubmit);
        CreateButton.Visibility = _reviewing && !_running && (!_started || eligible > 0) ? Visibility.Visible : Visibility.Collapsed;
        CreateButton.IsEnabled = !busy && !_running && eligible > 0 &&
            (_started || ExcludeInvalidCheckBox.IsChecked == true || _rows.All(row => row.State != CardImportState.Invalid));
        CreateButton.Content = _started ? $"Continuar / reintentar {eligible}" : $"Crear {eligible} tarjetas en Trello";
        StopButton.Visibility = _running ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = !_stopRequested;
        ExportButton.Visibility = _started ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = !_running && !_savingFile;
        BackButton.IsEnabled = !_running;
        BackButton.Content = _started ? "Mis tarjetas" : "Cancelar";
        BackButton.Visibility = _reviewing && !_started ? Visibility.Collapsed : Visibility.Visible;
        NewImportButton.Visibility = _started && !_running ? Visibility.Visible : Visibility.Collapsed;
        NewImportButton.IsEnabled = !_savingFile;
        SourceSummaryPanel.Visibility = _started ? Visibility.Collapsed : Visibility.Visible;
        ValidationBadges.Visibility = _started ? Visibility.Collapsed : Visibility.Visible;
        ResultSummaryGrid.Visibility = _started ? Visibility.Visible : Visibility.Collapsed;
        ResultTableTitle.Visibility = _started ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Visibility = _running || busy ? Visibility.Visible : Visibility.Collapsed;
        PageDescription.Text = _started ? "Consulta qué tarjetas se crearon y recupera las que quedaron pendientes."
            : _reviewing ? "Comprueba los datos y resuelve los errores antes de enviar a Trello."
            : "Importa un CSV o genera propuestas con Gemini y elige el destino.";
        FooterHint.Text = _started ? eligible > 0
                ? "Solo se reenviarán pendientes o rechazadas. Descarga los resultados antes de salir."
                : "Descarga los resultados antes de iniciar otra importación o salir."
            : _reviewing ? "La creación se habilita al corregir o excluir los errores."
            : "Siguiente: revisar los datos antes de crear.";
        VisualStateManager.GoToState(this, _started ? "ResultStep" : _reviewing ? "ReviewStep" : "FileStep", false);
        for (int index = 0; index < _rows.Count; index++)
            if (RowsList.ContainerFromIndex(index) is ListViewItem { ContentTemplateRoot: Control rowControl }) ApplyRowVisuals(rowControl);
        if (_started)
        {
            ImportProgress.Visibility = _running ? Visibility.Visible : Visibility.Collapsed;
            ImportProgress.IsIndeterminate = false;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        SummaryText.Visibility = _reviewing ? Visibility.Visible : Visibility.Collapsed;
        int invalid = _rows.Count(row => row.State == CardImportState.Invalid);
        ValidationInfoBar.IsOpen = _reviewing && !_started && invalid > 0;
        ValidationInfoBar.Visibility = ValidationInfoBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        ValidationInfoBar.Title = $"Hay {invalid} fila(s) que necesitan corrección.";
        CorrectFileButton.Visibility = _reviewing && !_started && !_sourceIsAi && invalid > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_started)
        {
            ReadCountText.Text = $"{_rows.Count} filas leídas";
            ValidCountText.Text = $"{_rows.Count(row => row.CanSubmit)} válidas";
            InvalidCountText.Text = $"{invalid} errores";
            SummaryText.Text = $"Mostrando {_rows.Count} filas · {invalid} con errores";
            return;
        }
        int created = _rows.Count(row => row.State == CardImportState.Created);
        int rejected = _rows.Count(row => row.State == CardImportState.Rejected);
        int uncertain = _rows.Count(row => row.State == CardImportState.Uncertain);
        int excluded = _rows.Count(row => row.State == CardImportState.Excluded);
        CreatedCountText.Text = created.ToString();
        PendingCountText.Text = (_rows.Count(row => row.CanSubmit || row.State == CardImportState.Creating) + uncertain).ToString();
        PendingCountLabel.Text = uncertain > 0 ? "Pendientes y por revisar" : "Pendientes de reintento";
        ExcludedCountText.Text = excluded.ToString();
        SummaryText.Text = $"{created} creadas · {rejected} rechazadas · {uncertain} inciertas · {_rows.Count(row => row.State == CardImportState.Ready)} pendientes · {excluded} excluidas";
        ImportProgress.Value = _rows.Count == 0 ? 0 : 100.0 * (created + rejected + uncertain + excluded) / _rows.Count;
    }

    private void SetLoading(bool loading, string message = "")
    {
        ImportProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ImportProgress.IsIndeterminate = loading;
        ProgressText.Text = message;
        ProgressText.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        ImportInfoBar.Title = _started ? "Resultado de la importación" : string.Empty;
        ImportInfoBar.Message = message;
        ImportInfoBar.Severity = severity;
        ImportInfoBar.IsOpen = true;
    }
}
