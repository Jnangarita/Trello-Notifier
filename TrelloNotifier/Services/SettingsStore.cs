using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrelloNotifier.Models;

namespace TrelloNotifier.Services;

internal sealed class SettingsStore
{
    private const int SchemaVersion = 1;
    private readonly string _directoryPath;

    public SettingsStore(string? directoryPath = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Trello Notifier");
    }

    public AppSettings Load() => WithConnection(connection =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ApiBaseUrl, ApiKey, Token, NotifyBeforeMinutes, PollIntervalMinutes,
                   RepeatReminderMinutes, IncludeOverdueCards, PlaySound, Theme
            FROM AppSettings WHERE Id = 1;";
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new IOException("La base de datos no contiene la configuración esperada.");
        }

        return new AppSettings
        {
            ApiBaseUrl = reader.GetString(0),
            ApiKey = reader.GetString(1),
            Token = reader.GetString(2),
            NotifyBeforeMinutes = reader.GetInt32(3),
            PollIntervalMinutes = reader.GetInt32(4),
            RepeatReminderMinutes = reader.GetInt32(5),
            IncludeOverdueCards = reader.GetBoolean(6),
            PlaySound = reader.GetBoolean(7),
            Theme = reader.GetString(8)
        };
    });

    public void Save(AppSettings settings) => WithConnection(connection =>
    {
        WriteSettings(connection, null, settings);
        return true;
    });

    public Dictionary<string, DateTimeOffset> LoadNotifiedCards() => WithConnection(connection =>
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT CardKey, LastNotifiedAt FROM NotificationHistory;";
        using SqliteDataReader reader = command.ExecuteReader();
        var history = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (!DateTimeOffset.TryParseExact(reader.GetString(1), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset lastNotified))
            {
                throw new IOException("El historial de la base de datos contiene una fecha inválida.");
            }
            history.Add(reader.GetString(0), lastNotified);
        }
        return history;
    });

    public void SaveNotifiedCards(Dictionary<string, DateTimeOffset> notifiedCards) => WithConnection(connection =>
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM NotificationHistory;";
        command.ExecuteNonQuery();
        WriteHistory(connection, transaction, notifiedCards);
        transaction.Commit();
        return true;
    });

    private T WithConnection<T>(Func<SqliteConnection, T> action)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directoryPath, "trello-notifier.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            connection.Open();
            InitializeDatabase(connection);
            return action(connection);
        }
        catch (SqliteException ex)
        {
            // No propagar SQL, valores ni excepciones internas a la UI o al log global.
            throw new IOException(ex.SqliteErrorCode is 5 or 6
                ? "La base de datos está ocupada. Vuelve a intentarlo en unos segundos."
                : "No se pudo leer o guardar la base de datos local. Revisa los permisos y el espacio disponible; conserva el archivo para recuperarlo.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException("No hay permisos para acceder a los datos locales de Trello Notifier.");
        }
    }

    private void InitializeDatabase(SqliteConnection connection)
    {
        if (ReadSchemaVersion(connection, null) == SchemaVersion) return;

        // BEGIN IMMEDIATE serializa también la primera apertura desde dos instancias.
        using SqliteTransaction transaction = connection.BeginTransaction();
        if (ReadSchemaVersion(connection, transaction) == SchemaVersion)
        {
            transaction.Commit();
            return;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            CREATE TABLE AppSettings (
                Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
                ApiBaseUrl TEXT NOT NULL,
                ApiKey TEXT NOT NULL,
                Token TEXT NOT NULL,
                NotifyBeforeMinutes INTEGER NOT NULL,
                PollIntervalMinutes INTEGER NOT NULL,
                RepeatReminderMinutes INTEGER NOT NULL,
                IncludeOverdueCards INTEGER NOT NULL CHECK (IncludeOverdueCards IN (0, 1)),
                PlaySound INTEGER NOT NULL CHECK (PlaySound IN (0, 1)),
                Theme TEXT NOT NULL
            );
            CREATE TABLE NotificationHistory (
                CardKey TEXT NOT NULL PRIMARY KEY,
                LastNotifiedAt TEXT NOT NULL
            );";
        command.ExecuteNonQuery();

        AppSettings settings = ReadLegacySettings();
        Dictionary<string, DateTimeOffset> history = ReadLegacyHistory();
        WriteSettings(connection, transaction, settings);
        WriteHistory(connection, transaction, history);
        command.CommandText = "PRAGMA user_version = 1;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version is not (0 or SchemaVersion))
        {
            throw new IOException("La versión de la base de datos no es compatible con esta aplicación. No se han modificado los datos.");
        }
        return version;
    }

    private static void WriteSettings(SqliteConnection connection, SqliteTransaction? transaction, AppSettings settings)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO AppSettings (Id, ApiBaseUrl, ApiKey, Token, NotifyBeforeMinutes, PollIntervalMinutes,
                                     RepeatReminderMinutes, IncludeOverdueCards, PlaySound, Theme)
            VALUES (1, $url, $key, $token, $before, $poll, $repeat, $overdue, $sound, $theme)
            ON CONFLICT(Id) DO UPDATE SET
                ApiBaseUrl = excluded.ApiBaseUrl, ApiKey = excluded.ApiKey, Token = excluded.Token,
                NotifyBeforeMinutes = excluded.NotifyBeforeMinutes, PollIntervalMinutes = excluded.PollIntervalMinutes,
                RepeatReminderMinutes = excluded.RepeatReminderMinutes, IncludeOverdueCards = excluded.IncludeOverdueCards,
                PlaySound = excluded.PlaySound, Theme = excluded.Theme;";
        command.Parameters.AddWithValue("$url", settings.ApiBaseUrl);
        command.Parameters.AddWithValue("$key", settings.ApiKey);
        command.Parameters.AddWithValue("$token", settings.Token);
        command.Parameters.AddWithValue("$before", settings.NotifyBeforeMinutes);
        command.Parameters.AddWithValue("$poll", settings.PollIntervalMinutes);
        command.Parameters.AddWithValue("$repeat", settings.RepeatReminderMinutes);
        command.Parameters.AddWithValue("$overdue", settings.IncludeOverdueCards);
        command.Parameters.AddWithValue("$sound", settings.PlaySound);
        command.Parameters.AddWithValue("$theme", settings.Theme);
        command.ExecuteNonQuery();
    }

    private static void WriteHistory(SqliteConnection connection, SqliteTransaction transaction,
        Dictionary<string, DateTimeOffset> history)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO NotificationHistory (CardKey, LastNotifiedAt) VALUES ($key, $last);";
        SqliteParameter key = command.Parameters.Add("$key", SqliteType.Text);
        SqliteParameter last = command.Parameters.Add("$last", SqliteType.Text);
        foreach (var entry in history)
        {
            key.Value = entry.Key;
            last.Value = entry.Value.ToString("O", CultureInfo.InvariantCulture);
            command.ExecuteNonQuery();
        }
    }

    private AppSettings ReadLegacySettings()
    {
        string path = Path.Combine(_directoryPath, "settings.json");
        try
        {
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new JsonException();
            if (settings.ApiBaseUrl is null || settings.ApiKey is null || settings.Token is null || settings.Theme is null)
            {
                throw new JsonException();
            }
            return settings;
        }
        catch (FileNotFoundException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            throw new IOException("No se pudo importar settings.json porque su contenido no es válido. Corrige el archivo y vuelve a intentarlo; los originales se conservan.");
        }
    }

    private Dictionary<string, DateTimeOffset> ReadLegacyHistory()
    {
        string path = Path.Combine(_directoryPath, "notified-cards.json");
        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                DateTimeOffset migratedAt = DateTimeOffset.UtcNow;
                HashSet<string> keys = JsonSerializer.Deserialize<HashSet<string>>(json) ?? throw new JsonException();
                if (keys.Any(key => key is null)) throw new JsonException();
                return keys.ToDictionary(key => key, _ => migratedAt, StringComparer.Ordinal);
            }
            return JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? throw new JsonException();
        }
        catch (FileNotFoundException)
        {
            return new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            throw new IOException("No se pudo importar notified-cards.json porque su contenido no es válido. Corrige el archivo y vuelve a intentarlo; los originales se conservan.");
        }
    }
}
