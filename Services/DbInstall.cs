using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Threading.Tasks;

namespace UpdateServer.Services
{
    /// <summary>
    /// Provides functionality to install and set up the database for the Update Server.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the DbInstall class.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    public class DbInstall(IConfiguration configuration)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        /// <summary>
        /// Asynchronously installs the database, creating necessary tables and stored procedures.
        /// </summary>
        /// <returns>A boolean indicating whether the installation was successful.</returns>
        public async Task<bool> InstallDbAsync()
        {
            try
            {
                Log.Information("Starting database installation process");
                CheckDatabasePermissions();
                await EnsureDatabaseExistsAsync();

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string[] requiredTables = ["Logs", "Users", "Updates_Client", "Updates_Games", "Performance"];
                foreach (var tableName in requiredTables)
                {
                    await TableCleanupAsync(connection, tableName);
                }

                await CreateCheckLogCountFunctionAsync(connection);
                await CreateRotateLogsProcedureAsync(connection);

                Log.Information("Database installation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error installing database");
                return false;
            }
        }

        /// <summary>
        /// Ensures that the database exists, creating it if necessary.
        /// </summary>
        public async Task<bool> EnsureDatabaseExistsAsync()
        {
            try
            {
                // Use the master database to create a new database
                var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
                {
                    InitialCatalog = "master"
                }.ConnectionString;

                await using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();

                string databaseName = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
                var checkDbCommand = new SqlCommand($"SELECT DB_ID('{databaseName}')", connection);

                var result = await checkDbCommand.ExecuteScalarAsync();

                if (result == DBNull.Value)
                {
                    // Database doesn't exist, so create it
                    var createDbCommand = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection);
                    await createDbCommand.ExecuteNonQueryAsync();
                    Log.Information($"Database '{databaseName}' created successfully");
                }
                else
                {
                    Log.Information($"Database '{databaseName}' already exists");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error ensuring database exists");
                return false;
            }
        }

        private void CheckDatabasePermissions()
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            if (builder.UserID != "sa" && !string.IsNullOrEmpty(builder.UserID))
            {
                Log.Warning("The current user may not have permissions to create a database. If database creation fails, please ensure the user has the necessary permissions or create the database manually.");
            }
        }

        private static async Task TableCleanupAsync(SqlConnection connection, string tableName)
        {
            // Check if table exists
            string query = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableName", tableName);
            var result = await command.ExecuteScalarAsync();
            if (result != null && Convert.ToInt32(result) > 0)
            {
                Log.Warning("Table {TableName} already exists. Deleting...", tableName);
                await DropTableAsync(connection, tableName);
            }

            Log.Information("Creating table {TableName}", tableName);
            await CreateTableAsync(connection, tableName);
        }

        private static async Task DropTableAsync(SqlConnection connection, string tableName)
        {
            string dropQuery = $"DROP TABLE {tableName}";
            await using var command = new SqlCommand(dropQuery, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task CreateTableAsync(SqlConnection connection, string tableName)
        {
            string createTableQuery = GetCreateTableQuery(tableName);
            await using var command = new SqlCommand(createTableQuery, connection);
            await command.ExecuteNonQueryAsync();
            Log.Information("Table {TableName} created successfully", tableName);

            // Create indexes for specific tables
            if (tableName is "Updates_Games" or "Performance" or "Logs")
            {
                string createIndexQuery = tableName switch
                {
                    "Updates_Games" => "CREATE INDEX IX_Updates_Games_GameId ON Updates_Games (GameId)",
                    "Performance" => "CREATE INDEX IX_Performance_Timestamp ON Performance (Timestamp)",
                    "Logs" => "CREATE INDEX IX_Logs_Timestamp ON Logs (Timestamp)",
                    _ => throw new ArgumentException($"Unexpected table name: {tableName}")
                };

                await using var indexCommand = new SqlCommand(createIndexQuery, connection);
                await indexCommand.ExecuteNonQueryAsync();
                Log.Information($"Index created on {tableName}");
            }
        }

        private static string GetCreateTableQuery(string tableName) => tableName switch
        {
            "Logs" => @"
                CREATE TABLE Logs (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Timestamp DATETIME2 DEFAULT GETUTCDATE(),
                    LogLevel NVARCHAR(50),
                    Message NVARCHAR(MAX)
                )",
            "Users" => @"
                CREATE TABLE Users (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Username NVARCHAR(100) UNIQUE NOT NULL,
                    PasswordHash NVARCHAR(255) NOT NULL,
                    IsAdmin BIT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
                    LastLogin DATETIME2,
                    LastLoginIP NVARCHAR(45)
                )",
            "Updates_Client" => @"
                CREATE TABLE Updates_Client (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Name NVARCHAR(255) NOT NULL,
                    Version NVARCHAR(50) NOT NULL,
                    Size BIGINT NOT NULL,
                    Hash NVARCHAR(64) NOT NULL,
                    UploadDate DATETIME2 DEFAULT GETUTCDATE(),
                    FilePath NVARCHAR(1000) NOT NULL,
                    ReleaseNotes NVARCHAR(MAX) NULL
                )",
            "Updates_Games" => @"
                CREATE TABLE Updates_Games (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    GameId INT NOT NULL,
                    RelativePath NVARCHAR(1000) NOT NULL,
                    Name NVARCHAR(255) NOT NULL,
                    Size BIGINT NOT NULL,
                    Hash NVARCHAR(64) NOT NULL,
                    UploadDate DATETIME2 DEFAULT GETUTCDATE(),
                    FileChangeDate DATETIME2 NOT NULL,
                    FilePath NVARCHAR(1000) NOT NULL
                )",
            "Performance" => @"
                CREATE TABLE Performance (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Timestamp DATETIME2 DEFAULT GETUTCDATE(),
                    CPUUsage_Pct FLOAT,
                    MemoryUsageKB BIGINT,
                    DiskUsage_Pct BIGINT,
                    ActiveConnections INT,
                    RequestsPerSecond INT,
                    AverageResponseTime_Ms FLOAT
                )",
            _ => throw new ArgumentException($"Unknown table name: {tableName}")
        };

        private static async Task CreateCheckLogCountFunctionAsync(SqlConnection connection)
        {
            string query = @"
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CheckLogCount]') AND type in (N'FN', N'IF', N'TF', N'FS', N'FT'))
    BEGIN
        EXEC('
        CREATE FUNCTION dbo.CheckLogCount()
        RETURNS BIT
        AS
        BEGIN
            DECLARE @Count INT;
            SELECT @Count = COUNT(*) FROM dbo.Logs;
            RETURN CASE WHEN @Count >= 100000 THEN 1 ELSE 0 END;
        END
        ')
    END";

            using var command = new SqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
            Log.Information("CheckLogCount function created or already exists.");
        }

        private static async Task CreateRotateLogsProcedureAsync(SqlConnection connection)
        {
            string query = @"
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RotateLogs]') AND type in (N'P', N'PC'))
    BEGIN
        EXEC('
        CREATE PROCEDURE dbo.RotateLogs
        AS
        BEGIN
            SET NOCOUNT ON;

            IF dbo.CheckLogCount() = 1
            BEGIN
                DECLARE @BackupTableName NVARCHAR(100);
                SET @BackupTableName = ''Logs_bak_'' + REPLACE(CONVERT(NVARCHAR, GETDATE(), 120), '':'', ''_'');

                EXEC sp_rename ''dbo.Logs'', @BackupTableName;

                CREATE TABLE dbo.Logs (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Timestamp DATETIME2 NOT NULL,
                    LogLevel NVARCHAR(50) NOT NULL,
                    Message NVARCHAR(MAX) NOT NULL
                );

                PRINT ''Log rotation completed. Old table renamed to: '' + @BackupTableName;
            END
            ELSE
            BEGIN
                PRINT ''Log rotation not needed. Current row count is less than 100,000.'';
            END
        END
        ')
    END";

            using var command = new SqlCommand(query, connection);
            await command.ExecuteNonQueryAsync();
            Log.Information("RotateLogs procedure created or already exists.");
        }

    }
}