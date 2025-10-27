using CounterStrikeSharp.API;
using MySqlConnector;

namespace Tags;

public class DatabaseService
{
    private readonly string? _connectionString;
    private readonly string _tableName;
    private readonly bool _enabled;

    public DatabaseService(Database config)
    {
        _enabled = config.Enabled;
        _tableName = config.TableName;
        
        if (!_enabled)
            return;

        _connectionString = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            UserID = config.User,
            Password = config.Password,
            Database = config.DatabaseName
        }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        if (!_enabled)
            return;

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var createTableQuery = $@"
                CREATE TABLE IF NOT EXISTS `{_tableName}` (
                    `SteamID` BIGINT UNSIGNED NOT NULL PRIMARY KEY,
                    `playerName` VARCHAR(128) NOT NULL,
                    `chatColor` VARCHAR(64) DEFAULT NULL,
                    `nameColor` VARCHAR(64) DEFAULT NULL,
                    `updatedAt` TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                )";

            await using var command = new MySqlCommand(createTableQuery, connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[cs2-tags] Database initialization error: {ex.Message}");
        }
    }

    public async Task<(string? chatColor, string? nameColor)> LoadPlayerColorsAsync(ulong steamId)
    {
        if (!_enabled)
            return (null, null);

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = $"SELECT `chatColor`, `nameColor` FROM `{_tableName}` WHERE `SteamID` = @steamId";
            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@steamId", steamId);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var chatColor = reader.IsDBNull(0) ? null : reader.GetString(0);
                var nameColor = reader.IsDBNull(1) ? null : reader.GetString(1);
                return (chatColor, nameColor);
            }
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[cs2-tags] Error loading player colors: {ex.Message}");
        }

        return (null, null);
    }

    public async Task SavePlayerColorsAsync(ulong steamId, string playerName, string? chatColor, string? nameColor)
    {
        if (!_enabled)
            return;

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = $@"
                INSERT INTO `{_tableName}` (`SteamID`, `playerName`, `chatColor`, `nameColor`)
                VALUES (@steamId, @playerName, @chatColor, @nameColor)
                ON DUPLICATE KEY UPDATE 
                    `playerName` = @playerName,
                    `chatColor` = @chatColor,
                    `nameColor` = @nameColor";

            await using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@steamId", steamId);
            command.Parameters.AddWithValue("@playerName", playerName);
            command.Parameters.AddWithValue("@chatColor", chatColor ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@nameColor", nameColor ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[cs2-tags] Error saving player colors: {ex.Message}");
        }
    }
}
