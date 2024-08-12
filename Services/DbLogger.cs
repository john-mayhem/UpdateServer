using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace UpdateServer.Services
{
    /// <summary>
    /// Handles database logging operations for the Update Server.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the DbLogger class.
    /// </remarks>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The hosting environment.</param>
    public partial class DbLogger(IConfiguration configuration, IHostEnvironment environment)
    {
        private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        private readonly string _logDirectory = Path.Combine(environment.ContentRootPath, "logs");

        /// <summary>
        /// Processes the last log file, inserting entries into the database.
        /// </summary>
        public async Task ProcessLastLogFileAsync()
        {
            var lastLogFile = GetLastLogFile();
            if (lastLogFile == null)
            {
                Log.Information("No previous log file found.");
                return;
            }

            var logEntries = ParseLogFile(lastLogFile);
            if (logEntries.Count == 0)
            {
                Log.Information("Previous log file is empty.");
                return;
            }

            await InsertLogEntriesAsync(logEntries);
            ClearLogFile(lastLogFile);
        }

        private string? GetLastLogFile()
        {
            if (!Directory.Exists(_logDirectory))
            {
                return null;
            }
            Log.Debug("Searching for log files in: {LogDirectory}", _logDirectory);
            return Directory.GetFiles(_logDirectory, "log_*.txt")
                            .OrderByDescending(f => f)
                            .Skip(1)  // Skip the current log file
                            .FirstOrDefault();
        }

        [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(\w{3})\] (.+)$", RegexOptions.Compiled)]
        private static partial Regex LogEntryRegex();

        private static List<(DateTime Timestamp, string LogLevel, string Message)> ParseLogFile(string filePath)
        {
            var logEntries = new List<(DateTime, string, string)>();
            var logPattern = LogEntryRegex();

            foreach (var line in File.ReadLines(filePath))
            {
                var match = logPattern.Match(line);
                if (match.Success)
                {
                    var timestamp = DateTime.Parse(match.Groups[1].Value);
                    var logLevel = match.Groups[2].Value;
                    var message = match.Groups[3].Value;
                    logEntries.Add((timestamp, logLevel, message));
                }
            }

            Log.Information("Parsed {EntryCount} log entries from file", logEntries.Count);
            return logEntries;
        }

        private async Task InsertLogEntriesAsync(IEnumerable<(DateTime Timestamp, string LogLevel, string Message)> logEntries)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Call the RotateLogs stored procedure
                await using (var command = new SqlCommand("EXEC dbo.RotateLogs", connection, (SqlTransaction)transaction))
                {
                    await command.ExecuteNonQueryAsync();
                }

                using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, (SqlTransaction)transaction);

                bulkCopy.DestinationTableName = "Logs";
                bulkCopy.BatchSize = 10000;

                // Explicitly map the columns
                bulkCopy.ColumnMappings.Add("Timestamp", "Timestamp");
                bulkCopy.ColumnMappings.Add("LogLevel", "LogLevel");
                bulkCopy.ColumnMappings.Add("Message", "Message");

                var dataTable = new DataTable();
                dataTable.Columns.Add("Timestamp", typeof(DateTime));
                dataTable.Columns.Add("LogLevel", typeof(string));
                dataTable.Columns.Add("Message", typeof(string));

                foreach (var entry in logEntries)
                {
                    dataTable.Rows.Add(entry.Timestamp, entry.LogLevel, entry.Message);
                }

                try
                {
                    await bulkCopy.WriteToServerAsync(dataTable);
                    Log.Information("Inserted {EntryCount} log entries into the database", logEntries.Count());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error bulk inserting log entries into the database");
                    if (ex.InnerException != null)
                    {
                        Log.Error("Inner exception: {InnerErrorMessage}", ex.InnerException.Message);
                    }
                    throw; // Re-throw to trigger transaction rollback
                }
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Log.Error(ex, "Error during log rotation and insertion. Transaction rolled back");
                throw;
            }
        }

        private static void ClearLogFile(string filePath)
        {
            File.WriteAllText(filePath, string.Empty);
            Log.Information("Cleared contents of log file: {FilePath}", filePath);
        }
    }
}