using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TrelloNotifier.Models;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public sealed partial class CreateCardPage : Page
{
    private readonly ObservableCollection<TrelloChecklistDraft> _checklists = new();
    private CancellationTokenSource? _pageCancellation;
    private CancellationTokenSource? _boardCancellation;
    private AppSettings? _settings;
    private TrelloCard? _createdCard;
    private bool _loadingBoards;
    private bool _loadingBoardDetails;
    private bool _submitting;
    private bool _submissionUncertain;
    private bool _generating;
    private CancellationTokenSource? _geminiCancellation;
    private ContentDialog? _geminiDialog;

    public CreateCardPage()
    {
        InitializeComponent();
        ChecklistsEditor.ItemsSource = _checklists;
        DueTimePicker.Time = TimeSpan.FromHours(9);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _pageCancellation = new CancellationTokenSource();
        await LoadBoardsAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _geminiCancellation?.Cancel();
        _geminiDialog?.Hide();
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
        if (_submitting)
        {
            e.Cancel = true;
            ShowMessage("Espera a que termine la creación de la tarjeta.", InfoBarSeverity.Informational);
        }
    }

    private async void RetryLoad_Click(object sender, RoutedEventArgs e)
    {
        await LoadBoardsAsync();
    }

    private async Task LoadBoardsAsync()
    {
        if (_pageCancellation is null || _loadingBoards || _submitting || _generating) return;
        CancellationToken token = _pageCancellation.Token;
        _loadingBoards = true;
        _boardCancellation?.Cancel();
        _boardCancellation?.Dispose();
        _boardCancellation = null;
        _loadingBoardDetails = false;
        BoardComboBox.ItemsSource = null;
        BoardDetails.IsEnabled = false;
        CardForm.IsEnabled = false;
        RetryLoadButton.Visibility = Visibility.Collapsed;
        SetLoading(true, "Cargando tableros...");
        UpdateCreateButton();
        try
        {
            _settings = await Task.Run(AppServices.Settings.Load, token);
            token.ThrowIfCancellationRequested();
            if (!_settings.IsConfigured)
            {
                ShowMessage("Configura la conexión con Trello antes de crear una tarjeta.", InfoBarSeverity.Warning);
                return;
            }

            List<TrelloBoard> boards = await AppServices.Trello.GetCreationBoardsAsync(_settings, token);
            token.ThrowIfCancellationRequested();
            BoardComboBox.ItemsSource = boards.Where(board => board is not null && !string.IsNullOrWhiteSpace(board.Id))
                .OrderBy(board => board.Name).ToList();
            CardForm.IsEnabled = true;
            if (BoardComboBox.Items.Count == 0)
                ShowMessage("No hay tableros abiertos disponibles para esta cuenta.", InfoBarSeverity.Informational);
            else
                BoardComboBox.SelectedIndex = 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // La navegación cancela la carga.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            if (token.IsCancellationRequested) return;
            AppLog.WriteFailure("LoadCreationBoards", ex);
            ShowMessage("No se pudieron cargar los tableros. Revisa la conexión y los permisos e inténtalo de nuevo.", InfoBarSeverity.Error);
        }
        finally
        {
            _loadingBoards = false;
            if (!token.IsCancellationRequested)
            {
                if (!_loadingBoardDetails) SetLoading(false);
                RetryLoadButton.Visibility = Visibility.Visible;
                UpdateCreateButton();
            }
        }
    }

    private async void Board_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _boardCancellation?.Cancel();
        _boardCancellation?.Dispose();
        _boardCancellation = null;
        ListComboBox.ItemsSource = null;
        MembersList.ItemsSource = null;
        LabelsList.ItemsSource = null;
        BoardDetails.IsEnabled = false;
        if (_pageCancellation is null || _settings is null || BoardComboBox.SelectedItem is not TrelloBoard board)
        {
            _loadingBoardDetails = false;
            UpdateCreateButton();
            return;
        }

        _boardCancellation = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        CancellationToken token = _boardCancellation.Token;
        _loadingBoardDetails = true;
        SetLoading(true, "Cargando miembros y etiquetas...");
        UpdateCreateButton();
        try
        {
            Task<List<TrelloMember>> membersTask = AppServices.Trello.GetBoardMembersAsync(_settings, board.Id, token);
            Task<List<TrelloLabel>> labelsTask = AppServices.Trello.GetBoardLabelsAsync(_settings, board.Id, token);
            await Task.WhenAll(membersTask, labelsTask);
            token.ThrowIfCancellationRequested();
            List<TrelloMember> members = await membersTask;
            List<TrelloLabel> labels = await labelsTask;
            ListComboBox.ItemsSource = (board.Lists ?? new List<TrelloList>())
                .Where(list => list is not null && !string.IsNullOrWhiteSpace(list.Id)).ToList();
            MembersList.ItemsSource = members.Where(member => member is not null && !string.IsNullOrWhiteSpace(member.Id)).OrderBy(member => member.DisplayName).ToList();
            LabelsList.ItemsSource = labels.Where(label => label is not null && !string.IsNullOrWhiteSpace(label.Id)).OrderBy(label => label.DisplayName).ToList();
            MembersHelp.Text = MembersList.Items.Count == 0 ? "El tablero no tiene miembros disponibles." : "Selecciona uno o varios miembros del tablero.";
            LabelsHelp.Text = LabelsList.Items.Count == 0 ? "El tablero no tiene etiquetas." : "Puedes seleccionar varias etiquetas.";
            BoardDetails.IsEnabled = true;
            if (ListComboBox.Items.Count > 0) ListComboBox.SelectedIndex = 0;
            else ShowMessage("El tablero no tiene listas abiertas. Elige otro tablero o crea una lista en Trello.", InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Un cambio de tablero descarta la respuesta anterior.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (token.IsCancellationRequested) return;
            AppLog.WriteFailure("LoadCreationBoardDetails", ex);
            ShowMessage("No se pudieron cargar los datos del tablero. Vuelve a cargar o selecciona otro tablero.", InfoBarSeverity.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _loadingBoardDetails = false;
                SetLoading(false);
                UpdateCreateButton();
            }
        }
    }

    private void AddChecklist_Click(object sender, RoutedEventArgs e) =>
        _checklists.Add(new TrelloChecklistDraft());

    private void RemoveChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrelloChecklistDraft checklist }) _checklists.Remove(checklist);
    }

    private void ClearDueDate_Click(object sender, RoutedEventArgs e) => DueDatePicker.Date = null;

    private void CancelGemini_Click(object sender, RoutedEventArgs e) => _geminiCancellation?.Cancel();

    private async void GenerateGemini_Click(object sender, RoutedEventArgs e)
    {
        if (_generating || !GenerateGeminiButton.IsEnabled || _pageCancellation is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_pageCancellation.Token);
        _geminiCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        _generating = true;
        CardForm.IsEnabled = false;
        GeminiPromptTextBox.IsEnabled = false;
        RetryLoadButton.IsEnabled = false;
        CancelGeminiButton.Visibility = Visibility.Visible;
        UpdateCreateButton();
        SetLoading(true, "Generando propuesta con Gemini...");
        try
        {
            AppSettings settings = await Task.Run(AppServices.Settings.Load, token);
            List<CardImportRow> proposals = await AppServices.Gemini.GenerateAsync(settings, GeminiPromptTextBox.Text,
                true, DateTimeOffset.Now, TimeZoneInfo.Local, token);
            token.ThrowIfCancellationRequested();
            CardImportRow proposal = proposals[0];
            TrelloCardDraft draft = proposal.Draft;
            string preview = $"{draft.Name}\n\n{draft.Description}\n\nVencimiento: {proposal.DueText}\n{proposal.ReviewNote}";
            foreach (TrelloChecklistDraft checklist in draft.Checklists)
                preview += $"\n\n{checklist.Name}\n{checklist.ItemsText}";
            _geminiDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Revisar propuesta de Gemini",
                Content = new ScrollViewer
                {
                    MaxHeight = 400,
                    Content = new TextBlock { Text = "Aplicar reemplazará título, descripción, vencimiento y checklists del formulario.\n\n" + preview, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
                },
                PrimaryButtonText = "Aplicar y reemplazar",
                CloseButtonText = "Conservar formulario",
                DefaultButton = ContentDialogButton.Close
            };
            SetLoading(false);
            CancelGeminiButton.Visibility = Visibility.Collapsed;
            ContentDialogResult result = await _geminiDialog.ShowAsync();
            token.ThrowIfCancellationRequested();
            if (result != ContentDialogResult.Primary) return;
            TitleTextBox.Text = draft.Name;
            DescriptionTextBox.Text = draft.Description;
            DueDatePicker.Date = draft.Due?.ToLocalTime();
            if (draft.Due is DateTimeOffset due) DueTimePicker.Time = due.ToLocalTime().TimeOfDay;
            _checklists.Clear();
            foreach (TrelloChecklistDraft checklist in draft.Checklists) _checklists.Add(checklist);
            ShowMessage("Propuesta aplicada. Revisa los campos y el destino antes de crear. " + proposal.ReviewNote, InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelar o salir conserva el formulario anterior.
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or JsonException or IOException or OperationCanceledException)
        {
            if (_pageCancellation?.IsCancellationRequested == false)
            {
                AppLog.WriteFailure("GenerateManualGeminiCard", ex);
                ShowMessage(GeminiCardClient.GetErrorMessage(ex), InfoBarSeverity.Warning);
            }
        }
        finally
        {
            _geminiDialog = null;
            _geminiCancellation = null;
            _generating = false;
            if (_pageCancellation?.IsCancellationRequested == false)
            {
                CardForm.IsEnabled = true;
                GeminiPromptTextBox.IsEnabled = true;
                RetryLoadButton.IsEnabled = true;
                CancelGeminiButton.Visibility = Visibility.Collapsed;
                SetLoading(false);
                UpdateCreateButton();
            }
        }
    }

    private void FormatDescription_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string marker }) return;
        int start = DescriptionTextBox.SelectionStart;
        int length = DescriptionTextBox.SelectionLength;
        string selected = DescriptionTextBox.Text.Substring(start, length);
        string formatted = marker == "- "
            ? marker + selected.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n- ")
            : marker + selected + marker;
        DescriptionTextBox.Text = DescriptionTextBox.Text.Remove(start, length).Insert(start, formatted);
        DescriptionTextBox.Focus(FocusState.Programmatic);
        DescriptionTextBox.Select(start + marker.Length, selected.Length);
    }

    private bool TryGetDraft(out TrelloCardDraft draft)
    {
        draft = new TrelloCardDraft
        {
            Name = TitleTextBox.Text,
            Description = DescriptionTextBox.Text,
            ListId = (ListComboBox.SelectedItem as TrelloList)?.Id ?? string.Empty,
            MemberIds = MembersList.SelectedItems.OfType<TrelloMember>().Select(member => member.Id).ToList(),
            LabelIds = LabelsList.SelectedItems.OfType<TrelloLabel>().Select(label => label.Id).ToList(),
            Checklists = _checklists.ToList()
        };
        if (DueDatePicker.Date is DateTimeOffset date)
        {
            DateTime local = DateTime.SpecifyKind(date.Date.Add(DueTimePicker.Time), DateTimeKind.Unspecified);
            if (DueTimePicker.Time < TimeSpan.Zero || TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
            {
                ShowMessage("Selecciona una hora válida y sin ambigüedad por cambio de horario de Windows.", InfoBarSeverity.Warning);
                return false;
            }
            draft.Due = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        }
        string? error = draft.GetValidationError();
        if (error is null) return true;
        ShowMessage(error, InfoBarSeverity.Warning);
        return false;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting || _generating || _createdCard is not null || _submissionUncertain || _settings is null ||
            _pageCancellation is null || _loadingBoards || _loadingBoardDetails || !CardForm.IsEnabled || !BoardDetails.IsEnabled || !TryGetDraft(out TrelloCardDraft draft)) return;
        CancellationToken token = _pageCancellation.Token;
        _submitting = true;
        CardForm.IsEnabled = false;
        RetryLoadButton.IsEnabled = false;
        BackButton.IsEnabled = false;
        UpdateCreateButton();
        SetLoading(true, "Creando tarjeta...");
        try
        {
            _createdCard = await AppServices.Trello.CreateCardAsync(_settings, draft, token);
            OpenCreatedButton.Visibility = _createdCard.TrelloUri is not null ? Visibility.Visible : Visibility.Collapsed;
            foreach (TrelloChecklistDraft checklist in draft.Checklists)
            {
                SetLoading(true, "Añadiendo checklists a la tarjeta creada...");
                string checklistId = await AppServices.Trello.CreateChecklistAsync(_settings, _createdCard.Id, checklist.Name, token);
                foreach (string item in checklist.GetItems())
                    await AppServices.Trello.AddChecklistItemAsync(_settings, checklistId, item, token);
            }
            ShowMessage("Tarjeta creada correctamente. Vuelve a Mis tarjetas para actualizar la consulta; aparecerá si está asignada a ti.", InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
        {
            AppLog.WriteFailure("CreateManualCard", ex);
            if (_createdCard is not null)
            {
                ShowMessage("La tarjeta se creó, pero no se pudieron completar todos los checklists. Ábrela en Trello para revisar y completar lo pendiente.", InfoBarSeverity.Warning);
            }
            else if (TrelloApiClient.IsDefiniteCreationRejection(ex))
            {
                ShowMessage("Trello rechazó la creación. Revisa los campos, los permisos de escritura y el acceso a la lista; si alcanzaste el límite de solicitudes, espera antes de reintentar.", InfoBarSeverity.Error);
            }
            else
            {
                // Un timeout o una respuesta incompleta no demuestran que el POST haya fallado.
                _submissionUncertain = true;
                ShowMessage("No se pudo confirmar si la tarjeta se creó. Revisa el tablero en Trello antes de iniciar otra creación para evitar duplicados.", InfoBarSeverity.Warning);
            }
        }
        finally
        {
            _submitting = false;
            SetLoading(false);
            BackButton.IsEnabled = true;
            CardForm.IsEnabled = _createdCard is null && !_submissionUncertain;
            RetryLoadButton.IsEnabled = CardForm.IsEnabled;
            UpdateCreateButton();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => MainPage.Current?.NavigateToCards(TrelloCardScope.Pending);

    private void OpenCreated_Click(object sender, RoutedEventArgs e)
    {
        if (_createdCard?.TrelloUri is not Uri uri) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.WriteFailure("OpenCreatedCard", ex);
            ShowMessage("No se pudo abrir el navegador. Puedes consultar la tarjeta directamente en Trello.", InfoBarSeverity.Warning);
        }
    }

    private void UpdateCreateButton()
    {
        bool available = !_submitting && !_generating && !_loadingBoards && !_loadingBoardDetails &&
            _createdCard is null && !_submissionUncertain && CardForm.IsEnabled;
        CreateButton.IsEnabled = available && BoardDetails.IsEnabled && ListComboBox.Items.Count > 0;
        GenerateGeminiButton.IsEnabled = available;
    }

    private void SetLoading(bool loading, string message = "")
    {
        LoadingRing.IsActive = loading;
        LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Text = message;
        LoadingText.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        CreationInfoBar.Message = message;
        CreationInfoBar.Severity = severity;
        CreationInfoBar.IsOpen = true;
    }
}
