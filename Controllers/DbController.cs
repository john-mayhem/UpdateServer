using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Threading.Tasks;
using UpdateServer.Models;

namespace UpdateServer.Controllers
{
    /// <summary>
    /// Controller responsible for database operations and management.
    /// </summary>
    public class DbController
    {
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="DbController"/> class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        public DbController(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string 'DefaultConnection' cannot be null or empty.", nameof(configuration));
            }
            _connectionString = connectionString;
        }

        /// <summary>
        /// Initializes the database connection.
        /// </summary>
        /// <returns>A boolean indicating whether the initialization was successful.</returns>
        public async Task<bool> InitializeAsync()
        {
            return await AttemptConnectionAsync();
        }

        /// <summary>
        /// Attempts to establish a connection to the database.
        /// </summary>
        /// <returns>A boolean indicating whether the connection attempt was successful.</returns>
        private async Task<bool> AttemptConnectionAsync()
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                Log.Information("Successfully connected to the database");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error connecting to the database");
                return false;
            }
        }

        /// <summary>
        /// Checks if the database structure is correct and complete.
        /// </summary>
        /// <returns>A boolean indicating whether the database structure is valid.</returns>
        public async Task<bool> CheckDatabaseStructureAsync()
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string[] requiredTables = ["Logs", "Users", "Updates_Client", "Updates_Games", "Performance"];
                foreach (var tableName in requiredTables)
                {
                    if (!await TableExistsAsync(connection, tableName))
                    {
                        Log.Warning("Table {TableName} does not exist", tableName);
                        return false;
                    }
                }

                Log.Information("Database structure is valid");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking database structure");
                return false;
            }
        }

        /// <summary>
        /// Checks if a specific table exists in the database.
        /// </summary>
        /// <param name="connection">The SQL connection to use.</param>
        /// <param name="tableName">The name of the table to check.</param>
        /// <returns>A boolean indicating whether the table exists.</returns>
        private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName)
        {
            string query = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
            await using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableName", tableName);
            var result = await command.ExecuteScalarAsync();
            return result != null && Convert.ToInt32(result) > 0;
        }
    }
}