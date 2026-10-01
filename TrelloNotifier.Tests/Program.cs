using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrelloNotifier.Models;
using TrelloNotifier.Services;
using TrelloNotifier.Tests;

DateTimeOffset now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
int passed = 0;

await Run("Ventana de aviso, límites y tarjetas completadas", () =>
{
    TrelloCard[] cards =
    {
        Card("sin-fecha", null), Card("completada", now.AddMinutes(10), true),
        Card("vencida", now.AddMinutes(-1)), Card("vence-ahora", now),
        Card("proxima", now.AddSeconds(1)), Card("limite", now.AddMinutes(60)),
        Card("futura", now.AddMinutes(60).AddSeconds(1))
    };
    var settings = new AppSettings();
    Check(ReminderSchedule.GetEligibleCards(cards, settings, now).Select(c => c.Id)
        .SequenceEqual(new[] { "proxima", "limite" }), "Solo próximas pendientes dentro del límite.");
    settings.IncludeOverdueCards = true;
    Check(ReminderSchedule.GetEligibleCards(cards, settings, now).Count == 4, "Incluir vencidas y vencimiento exacto.");
    Check(new TrelloCardViewModel(cards[2], now).RemainingText == "Vencida pendiente", "Texto para vencidas.");
    return Task.CompletedTask;
});

await Run("Estados de todas las tarjetas y límites de vencimiento", () =>
{
    (TrelloCard Card, TrelloCardStatus Status)[] cases =
    {
        (Card("sin-fecha", null), TrelloCardStatus.NoDueDate),
        (Card("completada-sin-fecha", null, true), TrelloCardStatus.Completed),
        (Card("completada-vencida", now.AddDays(-1), true), TrelloCardStatus.Completed),
        (Card("completada-proxima", now.AddMinutes(10), true), TrelloCardStatus.Completed),
        (Card("vencida", now.AddTicks(-1)), TrelloCardStatus.Overdue),
        (Card("vence-ahora", now.ToOffset(TimeSpan.FromHours(-5))), TrelloCardStatus.Overdue),
        (Card("proxima", now.AddTicks(1)), TrelloCardStatus.DueSoon),
        (Card("limite", now.AddMinutes(60).ToOffset(TimeSpan.FromHours(2))), TrelloCardStatus.DueSoon),
        (Card("futura", now.AddMinutes(60).AddTicks(1)), TrelloCardStatus.Upcoming)
    };
    foreach (var item in cases)
    {
        Check(item.Card.GetStatus(now, 60) == item.Status, $"Estado de {item.Card.Id}.");
    }

    TrelloCard changing = Card("cambiante", now.AddMinutes(90));
    Check(changing.GetStatus(now, 60) == TrelloCardStatus.Upcoming, "Fuera de la ventana actual.");
    Check(changing.GetStatus(now, 120) == TrelloCardStatus.DueSoon, "Respetar anticipación personalizada.");
    Check(changing.GetStatus(now.AddMinutes(30), 60) == TrelloCardStatus.DueSoon, "Entrar en ventana al avanzar el tiempo.");
    Check(changing.GetStatus(now.AddMinutes(90), 60) == TrelloCardStatus.Overdue, "Vencer en el instante exacto.");
    return Task.CompletedTask;
});

await Run("Presentación de tarjetas sin fecha y completadas", () =>
{
    var noDueDate = new TrelloCardViewModel(Card("sin-fecha", null), now);
    Check(noDueDate.RemainingText == "Sin fecha" && noDueDate.DueText == "Sin fecha de vencimiento", "Mostrar tarjeta sin fecha sin fallar.");
    foreach (DateTimeOffset? due in new DateTimeOffset?[] { null, now.AddDays(-1), now.AddHours(1) })
    {
        TrelloCard card = Card("completada", due, true);
        var completed = new TrelloCardViewModel(card, now);
        Check(completed.RemainingText == "Completada", "Una completada no se presenta como pendiente.");
        Check(completed.Name == card.Name && completed.Url == card.Url, "Conservar identidad y enlace.");
    }
    Check(new TrelloCardViewModel(Card("inminente", now.AddSeconds(30)), now).RemainingText ==
        "Vence en menos de un minuto", "Conservar detalle del vencimiento próximo.");
    return Task.CompletedTask;
});

await Run("Nombre del tablero visible también en tarjetas completadas y sin fecha", () =>
{
    foreach (TrelloCard card in new[] { Card("pendiente", now.AddMinutes(10)), Card("completada", null, true), Card("sin-fecha", null) })
    {
        card.BoardName = "Equipo de diseño";
        card.BoardId = "tablero-prueba";
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero: Equipo de diseño", "Mostrar el nombre en cualquier estado.");
        card.BoardName = " ";
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero: tablero-prueba", "Usar el identificador si falta el nombre.");
        card.BoardId = string.Empty;
        Check(new TrelloCardViewModel(card, now).BoardText == "Tablero no disponible", "Informar ausencia de datos sin una etiqueta vacía.");
    }
    return Task.CompletedTask;
});

await Run("Insignias del dashboard respetan la anticipación y los estados especiales", () =>
{
    (TrelloCard Card, TrelloCardStatus Status, string Text)[] cases =
    {
        (Card("vencida", now), TrelloCardStatus.Overdue, "Vencida"),
        (Card("limite", now.AddMinutes(90)), TrelloCardStatus.DueSoon, "Próxima"),
        (Card("futura", now.AddMinutes(90).AddTicks(1)), TrelloCardStatus.Upcoming, "Al día"),
        (Card("sin-fecha", null), TrelloCardStatus.NoDueDate, "Sin fecha"),
        (Card("completada", now.AddDays(-1), true), TrelloCardStatus.Completed, "Completada")
    };
    foreach (var item in cases)
    {
        var viewModel = new TrelloCardViewModel(item.Card, now, 90);
        Check(viewModel.Status == item.Status && viewModel.StatusText == item.Text,
            $"La insignia de {item.Card.Id} coincide con la ventana del monitor.");
    }

    TrelloCard changing = Card("cambiante", now.AddMinutes(90));
    Check(new TrelloCardViewModel(changing, now, 60).StatusText == "Al día", "Fuera de una ventana más corta.");
    Check(new TrelloCardViewModel(changing, now.AddMinutes(90), 60).StatusText == "Vencida", "Actualizar el estado al vencer.");
    changing.BoardName = "Equipo de diseño";
    Check(new TrelloCardViewModel(changing, now).BoardName == "Equipo de diseño", "Columna de tablero sin prefijo.");
    changing.BoardName = null;
    changing.BoardId = "tablero-prueba";
    Check(new TrelloCardViewModel(changing, now).BoardName == "tablero-prueba", "Usar ID cuando falta el nombre.");
    changing.BoardId = string.Empty;
    Check(new TrelloCardViewModel(changing, now).BoardName == "Tablero no disponible", "Datos ausentes en la tabla.");
    return Task.CompletedTask;
});

await Run("Búsqueda parcial por nombre combinada con tablero y estado", () =>
{
    TrelloCard first = Card("primera", now.AddMinutes(10));
    first.Name = "Preparar entrega";
    first.BoardId = "a";
    TrelloCard second = Card("segunda", now.AddMinutes(20));
    second.Name = "Revisar ENTREGA";
    second.BoardId = "b";
    TrelloCard completed = Card("completada", null, true);
    completed.Name = "Entrega final";
    completed.BoardId = "a";
    TrelloCard other = Card("otra", null);
    other.Name = "Reunión semanal";
    other.BoardName = "Entrega";
    TrelloCard missingName = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"sin-nombre\",\"name\":null}")!;
    var snapshot = new MonitorSnapshot(true, "", now, new[] { first, second })
    {
        AssignedCards = new[] { completed, second, other, first, missingName },
        NotifyBeforeMinutes = 60
    };
    Check(snapshot.GetFilteredCards(null, null, now, "  eNtReGa  ").Select(card => card.Id)
        .SequenceEqual(new[] { "primera", "segunda", "completada" }), "Buscar fragmentos sin distinguir mayúsculas y mantener el orden.");
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.DueSoon, now, "entrega").Single() == first,
        "Combinar simultáneamente texto, tablero y estado.");
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.Completed, now, "final").Single() == completed,
        "Buscar también en completadas sin fecha.");
    Check(!snapshot.GetFilteredCards("b", null, now, "preparar").Any(), "No incluir coincidencias de otro tablero.");
    Check(!snapshot.GetFilteredCards(null, null, now, "inexistente").Any(), "Búsqueda sin resultados.");
    foreach (string? search in new string?[] { null, "", "  " })
    {
        Check(snapshot.GetFilteredCards(null, null, now, search).Count() == 5, "Vaciar la búsqueda restaura todas las tarjetas, incluso sin nombre.");
    }
    Check(snapshot.GetFilteredCards("a", TrelloCardStatus.DueSoon, now, "").Single() == first,
        "Vaciar la búsqueda conserva los otros filtros.");
    var refreshed = snapshot with { AssignedCards = new[] { second } };
    Check(refreshed.GetFilteredCards(null, null, now, "entrega").Single() == second, "Aplicar la búsqueda a datos actualizados.");
    Check(snapshot.AssignedCards.Count == 5 && snapshot.DueSoonCards.Count == 2, "Buscar no modifica tarjetas ni candidatas a avisos.");
    return Task.CompletedTask;
});

await Run("Filtro por tablero combinado con todos los estados", () =>
{
    var cards = new List<TrelloCard>();
    foreach (string boardId in new[] { "tablero-a", "tablero-b" })
    {
        TrelloCard[] boardCards =
        {
            Card(boardId + "-vencida", now), Card(boardId + "-proxima", now.AddMinutes(60)),
            Card(boardId + "-futura", now.AddMinutes(61)), Card(boardId + "-sin-fecha", null),
            Card(boardId + "-completada", now.AddMinutes(5), true)
        };
        foreach (TrelloCard card in boardCards)
        {
            card.BoardId = boardId;
            card.BoardName = "Mismo nombre";
        }
        cards.AddRange(boardCards);
    }
    cards.Add(Card("sin-tablero", null));
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = cards,
        NotifyBeforeMinutes = 60
    };
    Check(snapshot.GetFilteredCards(null, null, now).Count() == 11, "Todos incluye tarjetas sin datos de tablero.");
    Check(snapshot.GetFilteredCards("", null, now).Count() == 11, "La opción Todos los tableros no excluye tarjetas.");
    Check(snapshot.GetFilteredCards("tablero-a", null, now).Select(card => card.Id).SequenceEqual(new[]
    {
        "tablero-a-vencida", "tablero-a-completada", "tablero-a-proxima", "tablero-a-futura", "tablero-a-sin-fecha"
    }), "Filtrar por identificador y conservar el orden por vencimiento, con sin fecha al final.");
    foreach (TrelloCardStatus status in Enum.GetValues<TrelloCardStatus>())
    {
        TrelloCard selected = snapshot.GetFilteredCards("tablero-b", status, now).Single();
        Check(selected.BoardId == "tablero-b" && selected.GetStatus(now, 60) == status, "Combinar ambos filtros sin confundir nombres iguales.");
    }
    Check(!snapshot.GetFilteredCards("inexistente", null, now).Any(), "Un tablero sin tarjetas da resultado vacío.");
    Check(snapshot.GetFilteredCards("tablero-a", TrelloCardStatus.DueSoon, now.AddMinutes(60)).Single().Id == "tablero-a-futura",
        "Recalcular estados al filtrar usando la hora indicada.");
    Check(cards.Count == 11 && snapshot.AssignedCards.Count == 11 && snapshot.DueSoonCards.Count == 0,
        "Los filtros no modifican el snapshot ni las candidatas a avisos.");
    return Task.CompletedTask;
});

await Run("Opciones de tablero únicas, nombres ausentes y cambios de consulta", () =>
{
    TrelloCard first = Card("primera", null);
    first.BoardId = "a";
    TrelloCard second = Card("segunda", null);
    second.BoardId = "a";
    second.BoardName = "Alfa";
    TrelloCard third = Card("tercera", null);
    third.BoardId = "b";
    third.BoardName = "Alfa";
    TrelloCard unnamed = Card("sin-nombre", null);
    unnamed.BoardId = "c";
    unnamed.BoardName = " ";
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = new[] { unnamed, third, first, second, Card("sin-tablero", null) }
    };
    List<TrelloBoard> boards = snapshot.GetBoards();
    Check(boards.Select(board => board.Id).SequenceEqual(new[] { "a", "b", "c" }), "Una opción por ID, incluso con nombres repetidos.");
    Check(boards[0].Name == "Alfa" && boards[2].Name == "c", "Usar un nombre disponible o el ID como alternativa.");
    second.BoardName = "Renombrado";
    Check(snapshot.GetBoards().Single(board => board.Id == "a").Name == "Renombrado", "Actualizar nombres conservando la identidad.");
    var empty = snapshot with { AssignedCards = Array.Empty<TrelloCard>() };
    Check(empty.GetBoards().Count == 0 && !empty.GetFilteredCards(null, null, now).Any(), "Una consulta vacía retira tableros y tarjetas.");
    return Task.CompletedTask;
});

await Run("Campos de tablero compatibles con respuestas anteriores y valores nulos", () =>
{
    TrelloCard card = JsonSerializer.Deserialize<TrelloCard>("""
        { "id": "tarjeta", "name": "Prueba", "idBoard": "tablero-prueba", "due": null }
        """)!;
    TrelloBoard board = JsonSerializer.Deserialize<TrelloBoard>("""
        { "id": "tablero-prueba", "name": "Tablero de prueba" }
        """)!;
    Check(card.BoardId == board.Id && board.Name == "Tablero de prueba", "Leer identificadores y nombres de los contratos Trello.");
    foreach (string json in new[] { "{\"id\":\"anterior\"}", "{\"id\":\"nula\",\"idBoard\":null}" })
    {
        TrelloCard legacy = JsonSerializer.Deserialize<TrelloCard>(json)!;
        var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>()) { AssignedCards = new[] { legacy } };
        Check(snapshot.GetBoards().Count == 0 && snapshot.GetFilteredCards(null, null, now).Single() == legacy,
            "Mantener visibles tarjetas sin tablero en Todos sin crear opciones vacías.");
    }
    return Task.CompletedTask;
});

await Run("Mis tarjetas separa pendientes del historial y sus tableros", () =>
{
    TrelloCard overdue = Card("vencida", now);
    overdue.BoardId = "activo";
    TrelloCard soon = Card("proxima", now.AddMinutes(30));
    TrelloCard future = Card("futura", now.AddDays(1));
    TrelloCard noDueDate = Card("sin-fecha", null);
    TrelloCard completed = Card("completada", null, true);
    completed.BoardId = "terminado";
    TrelloCard archived = Card("archivada", now.AddMinutes(-1));
    archived.Closed = true;
    archived.BoardId = "archivo";
    TrelloCard both = Card("completada-archivada", now.AddMinutes(10), true);
    both.Closed = true;
    both.BoardId = "archivo";
    var snapshot = new MonitorSnapshot(true, "", now, Array.Empty<TrelloCard>())
    {
        AssignedCards = new[] { overdue, soon, future, noDueDate, completed, archived, both },
        NotifyBeforeMinutes = 60
    };

    Check(snapshot.GetCards(TrelloCardScope.Pending).Select(card => card.Id)
        .SequenceEqual(new[] { "vencida", "proxima", "futura", "sin-fecha" }), "Pendientes excluye completadas y archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.History).Count() == 3, "Historial incluye la unión, sin duplicar una completada archivada.");
    Check(snapshot.GetCards(TrelloCardScope.Completed).SequenceEqual(new[] { completed, both }), "Completadas incluye las archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.Archived).SequenceEqual(new[] { archived, both }), "Archivar no exige completar.");
    Check(snapshot.GetBoards(TrelloCardScope.Pending).Single().Id == "activo", "Tableros del dashboard solo con pendientes.");
    Check(snapshot.GetBoards(TrelloCardScope.History).Select(board => board.Id).OrderBy(id => id)
        .SequenceEqual(new[] { "archivo", "terminado" }), "Tableros del historial solo con completadas o archivadas.");
    Check(snapshot.GetFilteredCards("archivo", null, now, " COMPLETADA ", TrelloCardScope.History).Single() == both,
        "Combinar historial con tablero y búsqueda sin distinguir mayúsculas.");
    Check(!snapshot.GetFilteredCards("archivo", null, now, null, TrelloCardScope.Pending).Any(), "Un tablero solo histórico no aporta pendientes.");
    Check(snapshot.GetFilteredCards(null, TrelloCardStatus.DueSoon, now, null, TrelloCardScope.Pending).Single() == soon,
        "El filtro temporal no reincorpora completadas ni archivadas.");
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count(card => card.GetStatus(now, 60) is
        TrelloCardStatus.Upcoming or TrelloCardStatus.NoDueDate) == 2, "Al día solo incluye futuras y sin fecha pendientes.");

    completed.DueComplete = false;
    archived.Closed = false;
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count() == 6 && snapshot.GetCards(TrelloCardScope.History).Single() == both,
        "Reabrir y restaurar tarjetas las devuelve a pendientes según su estado actual.");
    var empty = snapshot with { AssignedCards = Array.Empty<TrelloCard>() };
    Check(!empty.GetCards(TrelloCardScope.History).Any() && empty.GetBoards(TrelloCardScope.History).Count == 0,
        "Un historial vacío no conserva tarjetas ni opciones anteriores.");
    return Task.CompletedTask;
});

await Run("Archivadas conservan el estado de completado y nunca generan avisos", () =>
{
    foreach (bool complete in new[] { false, true })
    {
        foreach (DateTimeOffset? due in new DateTimeOffset?[] { null, now.AddDays(-1), now.AddMinutes(5) })
        {
            TrelloCard card = Card("archivada", due, complete);
            card.Closed = true;
            var viewModel = new TrelloCardViewModel(card, now);
            Check(viewModel.IsArchived && viewModel.StatusText == "Archivada", "Identificar la tarjeta como archivada, sin urgencia temporal.");
            Check(viewModel.RemainingText == (complete ? "Completada y archivada" : "Archivada sin completar"),
                "Distinguir archivadas terminadas de archivadas sin completar.");
            Check(ReminderSchedule.GetEligibleCards(new[] { card }, new AppSettings { IncludeOverdueCards = true }, now).Count == 0,
                "Excluir archivadas incluso con avisos de vencidas activos.");
        }
    }
    TrelloCard legacy = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"anterior\"}")!;
    TrelloCard archived = JsonSerializer.Deserialize<TrelloCard>("{\"id\":\"archivada\",\"closed\":true,\"dueComplete\":false}")!;
    Check(!legacy.Closed && archived.Closed && !archived.DueComplete, "Leer closed de Trello y conservar compatibilidad si falta.");
    Check(JsonSerializer.Serialize(archived).Contains("\"closed\":true"), "Conservar el nombre externo del campo.");
    return Task.CompletedTask;
});

await Run("Primer aviso y repetición a los 15, 30 y 60 minutos", () =>
{
    TrelloCard card = Card("tarjeta", now.AddHours(4));
    var history = new Dictionary<string, DateTimeOffset>();
    var settings = new AppSettings();
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Primer aviso inmediato.");
    history[ReminderSchedule.GetKey(card)] = now;
    foreach (int interval in new[] { 15, 30, 60 })
    {
        settings.RepeatReminderMinutes = interval;
        Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(interval).AddTicks(-1)), "No adelantar el aviso.");
        Check(ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(interval)), "Avisar al cumplir el intervalo.");
    }
    card.Due = card.Due!.Value.AddMinutes(1);
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Un vencimiento nuevo permite un aviso nuevo.");
    return Task.CompletedTask;
});

await Run("Nunca desactiva repeticiones también para vencidas", () =>
{
    var settings = new AppSettings { RepeatReminderMinutes = 0, IncludeOverdueCards = true };
    TrelloCard card = Card("tarjeta", now.AddMinutes(10));
    var history = new Dictionary<string, DateTimeOffset>();
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now), "Nunca permite el primer aviso.");
    history[ReminderSchedule.GetKey(card)] = now;
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(5)), "No repetir próximas.");
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddDays(1)), "No repetir vencidas.");
    return Task.CompletedTask;
});

await Run("Al vencer se aplica el intervalo de 2 horas desde el último aviso", () =>
{
    var settings = new AppSettings { IncludeOverdueCards = true, RepeatReminderMinutes = 15 };
    TrelloCard card = Card("tarjeta", now.AddMinutes(10));
    var history = new Dictionary<string, DateTimeOffset> { [ReminderSchedule.GetKey(card)] = now };
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, now.AddMinutes(119)), "No repetir vencidas antes de dos horas.");
    Check(ReminderSchedule.ShouldNotify(card, settings, history, now.AddHours(2)), "Repetir vencidas a las dos horas.");
    return Task.CompletedTask;
});

await Run("Persistencia de preferencias y compatibilidad con configuración anterior", () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"), "{\"PollIntervalMinutes\":7}");
    AppSettings settings = fixture.Store.Load();
    Check(settings.PollIntervalMinutes == 7 && settings.RepeatReminderMinutes == 30 && !settings.IncludeOverdueCards,
        "Importar preferencias y aplicar valores predeterminados a propiedades ausentes.");
    settings.RepeatReminderMinutes = 0;
    settings.IncludeOverdueCards = true;
    fixture.Store.Save(settings);
    settings = new SettingsStore(fixture.DirectoryPath).Load();
    Check(settings.RepeatReminderMinutes == 0 && settings.IncludeOverdueCards, "Preferencias conservadas.");
    return Task.CompletedTask;
});

await Run("Migración del historial anterior y conservación tras reiniciar", () =>
{
    using var fixture = new Fixture(false);
    TrelloCard card = Card("anterior", now.AddHours(1));
    string key = ReminderSchedule.GetKey(card);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "notified-cards.json"), JsonSerializer.Serialize(new[] { key }));
    DateTimeOffset before = DateTimeOffset.UtcNow;
    var history = fixture.Store.LoadNotifiedCards();
    Check(history[key] >= before && history[key] <= DateTimeOffset.UtcNow, "Asignar hora de migración.");
    Check(new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards()[key] == history[key], "Migración persistida una sola vez.");
    var settings = new AppSettings { RepeatReminderMinutes = 0 };
    Check(!ReminderSchedule.ShouldNotify(card, settings, history, DateTimeOffset.UtcNow.AddDays(1)), "Nunca respeta el historial antiguo.");
    history[key] = now;
    fixture.Store.SaveNotifiedCards(history);
    Check(new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards()[key] == now, "Conservar hora exacta del último aviso.");
    return Task.CompletedTask;
});

await Run("SQLite conserva todas las preferencias sin crear archivos JSON", () =>
{
    using var fixture = new Fixture(false);
    AppSettings defaults = fixture.Store.Load();
    Check(JsonSerializer.Serialize(defaults) == JsonSerializer.Serialize(new AppSettings()), "Defaults de una instalación nueva.");
    Check(fixture.Store.LoadNotifiedCards().Count == 0, "Historial inicial vacío.");
    var expected = new AppSettings
    {
        ApiBaseUrl = "https://mock.example.com/api",
        ApiKey = "dummy-key-'ñ",
        Token = "dummy-token-'); DROP TABLE AppSettings; --",
        NotifyBeforeMinutes = 180,
        PollIntervalMinutes = 11,
        RepeatReminderMinutes = 60,
        IncludeOverdueCards = true,
        PlaySound = false,
        Theme = "Dark"
    };
    fixture.Store.Save(expected);
    AppSettings actual = new SettingsStore(fixture.DirectoryPath).Load();
    Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected), "Conservar todos los campos y parametrizar caracteres especiales.");
    Check(File.Exists(fixture.DatabasePath), "Crear la base SQLite.");
    Check(!File.Exists(Path.Combine(fixture.DirectoryPath, "settings.json")) &&
        !File.Exists(Path.Combine(fixture.DirectoryPath, "notified-cards.json")), "No crear JSON de persistencia.");
    return Task.CompletedTask;
});

await Run("Migración de ambos JSON conserva precisión y no vuelve a importar respaldos", () =>
{
    using var fixture = new Fixture(false);
    string settingsPath = Path.Combine(fixture.DirectoryPath, "settings.json");
    string historyPath = Path.Combine(fixture.DirectoryPath, "notified-cards.json");
    var expected = new AppSettings { ApiBaseUrl = "https://mock.example.com", Token = "dummy-token", Theme = "Light" };
    DateTimeOffset last = now.AddTicks(1234567).ToOffset(TimeSpan.FromMinutes(-330));
    var original = new Dictionary<string, DateTimeOffset> { ["Card'|123"] = last, ["card'|123"] = now };
    string settingsJson = JsonSerializer.Serialize(expected);
    string historyJson = JsonSerializer.Serialize(original);
    File.WriteAllText(settingsPath, settingsJson);
    File.WriteAllText(historyPath, historyJson);

    // Cualquier primera operación debe migrar ambos archivos, incluso leer el historial.
    var imported = fixture.Store.LoadNotifiedCards();
    Check(imported.Count == 2 && imported["Card'|123"].EqualsExact(last), "Mantener ticks, offset y claves sensibles a mayúsculas.");
    Check(JsonSerializer.Serialize(fixture.Store.Load()) == settingsJson, "Importar todas las preferencias.");
    Check(File.ReadAllText(settingsPath) == settingsJson && File.ReadAllText(historyPath) == historyJson, "No modificar los originales.");

    expected.Theme = "Dark";
    fixture.Store.Save(expected);
    fixture.Store.SaveNotifiedCards(new());
    File.WriteAllText(settingsPath, "JSON inválido posterior a la migración");
    File.WriteAllText(historyPath, "JSON inválido posterior a la migración");
    var restarted = new SettingsStore(fixture.DirectoryPath);
    Check(restarted.Load().Theme == "Dark" && restarted.LoadNotifiedCards().Count == 0,
        "SQLite es la fuente definitiva aunque los respaldos cambien o el historial se vacíe.");
    return Task.CompletedTask;
});

await Run("Migración inválida revierte el esquema y permite reintento sin pérdida", () =>
{
    foreach (var invalid in new[]
    {
        (Settings: "{", History: "{}"),
        (Settings: "null", History: "{}"),
        (Settings: "{\"Token\":null}", History: "{}"),
        (Settings: "{\"Theme\":\"Dark\"}", History: "{"),
        (Settings: "{}", History: "null"),
        (Settings: "{}", History: "[null]")
    })
    {
        using var fixture = new Fixture(false);
        string settingsPath = Path.Combine(fixture.DirectoryPath, "settings.json");
        string historyPath = Path.Combine(fixture.DirectoryPath, "notified-cards.json");
        File.WriteAllText(settingsPath, invalid.Settings);
        File.WriteAllText(historyPath, invalid.History);
        ExpectStorageFailure(() => fixture.Store.Load());
        Check(File.ReadAllText(settingsPath) == invalid.Settings && File.ReadAllText(historyPath) == invalid.History,
            "Preservar ambos archivos si falla la importación.");
        using (SqliteConnection connection = fixture.OpenDatabase())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "No confirmar migración fallida.");
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';";
            Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "Revertir también las tablas creadas.");
        }
        File.WriteAllText(settingsPath, "{\"Theme\":\"Light\"}");
        File.WriteAllText(historyPath, JsonSerializer.Serialize(new Dictionary<string, DateTimeOffset> { ["recuperada|123"] = now }));
        Check(fixture.Store.Load().Theme == "Light" && fixture.Store.LoadNotifiedCards()["recuperada|123"] == now,
            "Reintentar la importación completa después de corregir los archivos.");
    }
    return Task.CompletedTask;
});

await Run("Escritura de historial es atómica y errores SQL no exponen datos", () =>
{
    using var fixture = new Fixture();
    fixture.Store.SaveNotifiedCards(new() { ["original|123"] = now });
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TRIGGER RejectHistory BEFORE INSERT ON NotificationHistory
            WHEN NEW.CardKey = 'rechazada'
            BEGIN SELECT RAISE(ABORT, 'dummy-sensitive-detail'); END;";
        command.ExecuteNonQuery();
    }
    IOException error = ExpectStorageFailure(() => fixture.Store.SaveNotifiedCards(new()
    {
        ["primera"] = now.AddMinutes(1),
        ["rechazada"] = now.AddMinutes(2)
    }));
    Check(!error.ToString().Contains("dummy-sensitive-detail", StringComparison.Ordinal), "No propagar detalles del motor.");
    var history = new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards();
    Check(history.Count == 1 && history["original|123"] == now, "Deshacer borrado e inserciones parciales.");
    return Task.CompletedTask;
});

await Run("Base bloqueada informa error y permite guardar al liberar el bloqueo", () =>
{
    using var fixture = new Fixture();
    AppSettings settings = fixture.Store.Load();
    settings.Theme = "Dark";
    using (SqliteConnection connection = fixture.OpenDatabase())
    using (SqliteTransaction transaction = connection.BeginTransaction())
    {
        IOException error = ExpectStorageFailure(() => fixture.Store.Save(settings));
        Check(error.Message.Contains("ocupada", StringComparison.Ordinal), "Informar bloqueo con espera acotada.");
    }
    Check(fixture.Store.Load().Theme == "Default", "Una escritura bloqueada no modifica preferencias.");
    fixture.Store.Save(settings);
    Check(fixture.Store.Load().Theme == "Dark", "Reintentar tras liberar el bloqueo.");
    return Task.CompletedTask;
});

await Run("Versión futura y base corrupta se conservan sin reinicialización", () =>
{
    using var fixture = new Fixture();
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = 2;";
        command.ExecuteNonQuery();
    }
    ExpectStorageFailure(() => fixture.Store.Save(new AppSettings()));
    using (SqliteConnection connection = fixture.OpenDatabase())
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ApiBaseUrl FROM AppSettings WHERE Id = 1;";
        Check((string?)command.ExecuteScalar() == "https://mock.example.com", "No sobrescribir datos de una versión futura.");
    }

    using var corrupt = new Fixture(false);
    const string invalidDatabase = "Esto no es una base SQLite";
    File.WriteAllText(corrupt.DatabasePath, invalidDatabase);
    ExpectStorageFailure(() => corrupt.Store.Load());
    Check(File.ReadAllText(corrupt.DatabasePath) == invalidDatabase, "No borrar una base corrupta.");
    return Task.CompletedTask;
});

await Run("Primera apertura concurrente migra una sola vez", async () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "notified-cards.json"), "[\"anterior|123\"]");
    var histories = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        new SettingsStore(fixture.DirectoryPath).LoadNotifiedCards())));
    Check(histories.All(history => history.Count == 1 && history["anterior|123"] == histories[0]["anterior|123"]),
        "Todas las instancias observan una única migración confirmada.");
});

await Run("Fallo de almacenamiento es visible en monitor manual y automático", async () =>
{
    using var fixture = new Fixture(false);
    File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"), "{");
    using var monitor = fixture.CreateMonitor();
    var reported = new TaskCompletionSource<MonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Updated += snapshot => reported.TrySetResult(snapshot);
    await monitor.CheckNowAsync();
    Check((await reported.Task).HasError && fixture.Notifications.Batches.Count == 0, "Comprobación manual informa fallo sin avisar.");
    reported = new TaskCompletionSource<MonitorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Start();
    Check((await reported.Task.WaitAsync(TimeSpan.FromSeconds(10))).HasError, "El monitor automático informa el fallo de carga.");
});

await Run("Monitor agrupa avisos y evita duplicados concurrentes y tras reiniciar", async () =>
{
    using var fixture = new Fixture();
    fixture.Api.Cards = new[] { Card("uno", DateTimeOffset.UtcNow.AddMinutes(50)), Card("dos", DateTimeOffset.UtcNow.AddMinutes(55)) };
    using var monitor = fixture.CreateMonitor();
    await Task.WhenAll(monitor.CheckNowAsync(), monitor.CheckNowAsync());
    Check(fixture.Notifications.Batches.Count == 1 && fixture.Notifications.Batches[0].Count == 2, "Un único lote para dos tarjetas.");
    using var restarted = fixture.CreateMonitor();
    await restarted.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 1, "Reiniciar no duplica avisos.");
    var history = fixture.Store.LoadNotifiedCards();
    history[ReminderSchedule.GetKey(fixture.Api.Cards[0])] = DateTimeOffset.UtcNow.AddMinutes(-31);
    fixture.Store.SaveNotifiedCards(history);
    await restarted.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && fixture.Notifications.Batches[1].Single().Id == "uno", "Repetir solo la tarjeta cuyo intervalo transcurrió.");
});

await Run("Un aviso rechazado no consume el intervalo de repetición", async () =>
{
    using var fixture = new Fixture();
    fixture.Api.Cards = new[] { Card("uno", DateTimeOffset.UtcNow.AddMinutes(50)) };
    fixture.Notifications.Succeed = false;
    using var monitor = fixture.CreateMonitor();
    await monitor.CheckNowAsync();
    Check(fixture.Store.LoadNotifiedCards().Count == 0, "No guardar un aviso fallido.");
    fixture.Notifications.Succeed = true;
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && fixture.Store.LoadNotifiedCards().Count == 1, "Reintentar en la siguiente comprobación.");
});

await Run("Monitor publica todas las asignadas sin ampliar los avisos", async () =>
{
    using var fixture = new Fixture();
    AppSettings settings = fixture.Store.Load();
    settings.NotifyBeforeMinutes = 120;
    fixture.Store.Save(settings);
    DateTimeOffset checkTime = DateTimeOffset.UtcNow;
    fixture.Api.Cards = new[]
    {
        Card("vencida", checkTime.AddDays(-1)), Card("proxima", checkTime.AddMinutes(90)),
        Card("futura", checkTime.AddDays(1)), Card("sin-fecha", null),
        Card("completada", checkTime.AddMinutes(30), true)
    };
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, NotifyBeforeMinutes: 120, AssignedCards.Count: 5 }, "Publicar todas las tarjetas y la anticipación usada.");
    Check(snapshot!.AssignedCards.Select(c => c.Id).SequenceEqual(fixture.Api.Cards.Select(c => c.Id)), "El snapshot conserva todas las abiertas para filtrar localmente.");
    Check(snapshot.GetCards(TrelloCardScope.Pending).Count() == 4, "El dashboard excluye completadas del snapshot.");
    Check(snapshot.DueSoonCards.Single().Id == "proxima", "Separar candidatas de consulta completa.");
    Check(fixture.Notifications.Batches.Single().Single().Id == "proxima", "Solo avisar de la próxima pendiente.");

    fixture.Api.Fail = true;
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: true, AssignedCards.Count: 0 }, "No presentar resultados anteriores como actuales tras un fallo.");
    fixture.Api.Fail = false;
    fixture.Api.Cards = Array.Empty<TrelloCard>();
    await monitor.CheckNowAsync();
    Check(snapshot is { HasError: false, IsRunning: true, AssignedCards.Count: 0 }, "Una consulta vacía es válida y retira las tarjetas anteriores.");
    Check(fixture.Notifications.Batches.Count == 1, "Fallo y lista vacía no generan más avisos.");
});

await Run("Activar vencidas cambia avisos pero no las tarjetas consultables", async () =>
{
    using var fixture = new Fixture();
    TrelloCard card = Card("vencida", DateTimeOffset.UtcNow.AddDays(-1));
    fixture.Api.Cards = new[] { card };
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 0 && snapshot?.DueSoonCards.Count == 0, "Vencidas desactivadas por defecto.");
    Check(snapshot?.AssignedCards.Single().Id == "vencida", "Vencida visible aunque sus avisos estén desactivados.");
    AppSettings settings = fixture.Store.Load();
    settings.IncludeOverdueCards = true;
    fixture.Store.Save(settings);
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 1 && snapshot?.DueSoonCards.Single().Id == "vencida", "Primer aviso y presencia en la lista.");
    fixture.Store.SaveNotifiedCards(new() { [ReminderSchedule.GetKey(card)] = DateTimeOffset.UtcNow.AddMinutes(-121) });
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2, "Repetición de vencidas después de dos horas.");
    settings.IncludeOverdueCards = false;
    fixture.Store.Save(settings);
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches.Count == 2 && snapshot?.DueSoonCards.Count == 0, "Desactivar retira vencidas de las candidatas a avisos.");
    Check(snapshot?.AssignedCards.Single().Id == "vencida", "Desactivar avisos mantiene la vencida consultable.");
});

await Run("Fallo de API conserva historial y no emite notificaciones", async () =>
{
    using var fixture = new Fixture();
    fixture.Store.SaveNotifiedCards(new() { ["existente|123"] = now });
    fixture.Api.Fail = true;
    using var monitor = fixture.CreateMonitor();
    MonitorSnapshot? snapshot = null;
    monitor.Updated += value => snapshot = value;
    await monitor.CheckNowAsync();
    Check(snapshot?.HasError == true, "Informar fallo.");
    Check(fixture.Store.LoadNotifiedCards()["existente|123"] == now && fixture.Notifications.Batches.Count == 0, "No alterar historial ante fallo.");
});

await Run("Completadas, archivadas o ausentes dejan de avisar y un nuevo vencimiento se reevalúa", async () =>
{
    using var fixture = new Fixture();
    TrelloCard completed = Card("completada", DateTimeOffset.UtcNow.AddMinutes(40));
    TrelloCard removed = Card("ausente", DateTimeOffset.UtcNow.AddMinutes(45));
    TrelloCard changed = Card("cambiada", DateTimeOffset.UtcNow.AddMinutes(50));
    TrelloCard archived = Card("archivada", DateTimeOffset.UtcNow.AddMinutes(35));
    fixture.Api.Cards = new[] { completed, removed, changed, archived };
    using var monitor = fixture.CreateMonitor();
    await monitor.CheckNowAsync();
    completed.DueComplete = true;
    archived.Closed = true;
    changed.Due = changed.Due!.Value.AddMinutes(1);
    fixture.Api.Cards = new[] { completed, changed, archived };
    await monitor.CheckNowAsync();
    Check(fixture.Notifications.Batches[1].Single().Id == "cambiada", "Solo avisar del nuevo vencimiento.");
    Check(fixture.Store.LoadNotifiedCards().Count == 1, "Retirar claves completadas, archivadas, ausentes y del vencimiento anterior.");
});

await Run("Arquitectura: modelos independientes de servicios, UI e IO", () =>
{
    ArchitectureTests.ModelsAreIndependent();
    return Task.CompletedTask;
});

await Run("Arquitectura: calendario depende solo de modelos y BCL permitida", () =>
{
    ArchitectureTests.ScheduleDependsOnlyOnModels();
    return Task.CompletedTask;
});

await Run("Arquitectura: guard rechaza dependencias prohibidas y aliases", () =>
{
    ArchitectureTests.GuardRejectsForbiddenDependencies();
    return Task.CompletedTask;
});

Console.WriteLine($"{passed} pruebas correctas.");

async Task Run(string name, Func<Task> test)
{
    await test();
    passed++;
    Console.WriteLine($"OK: {name}");
}

static TrelloCard Card(string id, DateTimeOffset? due, bool complete = false) =>
    new() { Id = id, Name = id, Due = due, DueComplete = complete, Url = "https://trello.com/" };

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static IOException ExpectStorageFailure(Action action)
{
    try
    {
        action();
    }
    catch (IOException ex)
    {
        return ex;
    }
    throw new InvalidOperationException("Se esperaba un error de almacenamiento observable.");
}

sealed class Fixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "trello-reminder-tests-" + Guid.NewGuid());
    public string DatabasePath => Path.Combine(DirectoryPath, "trello-notifier.db");
    public SettingsStore Store { get; }
    public TrelloApiClient Api { get; } = new();
    public DesktopNotificationService Notifications { get; } = new();

    public Fixture(bool initialize = true)
    {
        Directory.CreateDirectory(DirectoryPath);
        Store = new SettingsStore(DirectoryPath);
        if (initialize) Store.Save(new AppSettings { ApiBaseUrl = "https://mock.example.com" });
    }

    public SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    public DueCardMonitor CreateMonitor() => new(Store, Api, Notifications);
    public void Dispose() => Directory.Delete(DirectoryPath, true);
}
