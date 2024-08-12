#pragma warning disable CA1416 // Validate platform compatibility

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace UpdateServer.Services
{
    /// <summary>
    /// Provides performance monitoring services for the Update Server.
    /// </summary>
    public class PerformanceService : BackgroundService
    {
        private readonly string _connectionString;
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _ramCounter;
        private readonly PerformanceCounter? _diskCounter;
        private readonly bool _isWindows;
        private PerformanceMetrics _latestMetrics = new();

        /// <summary>
        /// Initializes a new instance of the PerformanceService class.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        public PerformanceService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") + ";Database=UpdateServer;";
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            if (_isWindows)
            {
                try
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                    _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                    Log.Information("Performance counters initialized successfully");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error initializing performance counters. Performance monitoring will be disabled.");
                    _isWindows = false;
                }
            }
            else
            {
                Log.Warning("Performance monitoring is not supported on non-Windows platforms");
            }
        }

        /// <summary>
        /// Executes the performance monitoring service.
        /// </summary>
        /// <param name="stoppingToken">Triggered when the application is stopping.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isWindows)
            {
                Log.Information("Performance monitoring is disabled on non-Windows platforms or due to initialization errors.");
                return;
            }

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var metrics = CollectMetrics();
                        await StoreMetrics(metrics);
                        await Task.Delay(1000, stoppingToken); // Wait for 1 second
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Log.Error(ex, "Error in PerformanceService");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // This is expected when the application is shutting down, so we can ignore it
                Log.Information("PerformanceService is shutting down...");
            }
        }

        /// <summary>
        /// Collects the current performance metrics.
        /// </summary>
        /// <returns>The current performance metrics.</returns>
        private PerformanceMetrics CollectMetrics()
        {
            _latestMetrics = new PerformanceMetrics
            {
                CPUUsage_Pct = _cpuCounter?.NextValue() ?? 0,
                MemoryUsageKB = (long)(_ramCounter?.NextValue() ?? 0) * 1024,
                DiskUsage_Pct = _diskCounter?.NextValue() ?? 0,
                ActiveConnections = GetActiveConnections(),
                RequestsPerSecond = GetRequestsPerSecond(),
                AverageResponseTime_Ms = GetAverageResponseTime()
            };
            return _latestMetrics;
        }

        /// <summary>
        /// Gets the latest collected performance metrics.
        /// </summary>
        /// <returns>The latest performance metrics.</returns>
        public PerformanceMetrics GetLatestMetrics() => _latestMetrics;

        /// <summary>
        /// Stores the collected metrics in the database.
        /// </summary>
        /// <param name="metrics">The metrics to store.</param>
        private async Task StoreMetrics(PerformanceMetrics metrics)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            string query = @"
            INSERT INTO Performance (CPUUsage_Pct, MemoryUsageKB, DiskUsage_Pct, ActiveConnections, RequestsPerSecond, AverageResponseTime_Ms)
            VALUES (@CPUUsage_Pct, @MemoryUsageKB, @DiskUsage_Pct, @ActiveConnections, @RequestsPerSecond, @AverageResponseTime_Ms)";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CPUUsage_Pct", metrics.CPUUsage_Pct);
            command.Parameters.AddWithValue("@MemoryUsageKB", metrics.MemoryUsageKB);
            command.Parameters.AddWithValue("@DiskUsage_Pct", metrics.DiskUsage_Pct);
            command.Parameters.AddWithValue("@ActiveConnections", metrics.ActiveConnections);
            command.Parameters.AddWithValue("@RequestsPerSecond", metrics.RequestsPerSecond);
            command.Parameters.AddWithValue("@AverageResponseTime_Ms", metrics.AverageResponseTime_Ms);

            await command.ExecuteNonQueryAsync();
            Log.Debug("Performance metrics stored in database");
        }

        // TODO: Implement these methods based on your specific server setup
        private static int GetActiveConnections() 
        {
            // Placeholder implementation
            //Log.Warning("GetActiveConnections() is not implemented");
            return 0;
        }

        private static int GetRequestsPerSecond() 
        {
            // Placeholder implementation
            //Log.Warning("GetRequestsPerSecond() is not implemented");
            return 0;
        }

        private static float GetAverageResponseTime() 
        {
            // Placeholder implementation
            //Log.Warning("GetAverageResponseTime() is not implemented");
            return 0;
        }
    }

    /// <summary>
    /// Represents a set of performance metrics.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Gets or sets the CPU usage percentage.
        /// </summary>
        public float CPUUsage_Pct { get; set; }

        /// <summary>
        /// Gets or sets the memory usage in kilobytes.
        /// </summary>
        public long MemoryUsageKB { get; set; }

        /// <summary>
        /// Gets or sets the disk usage percentage.
        /// </summary>
        public float DiskUsage_Pct { get; set; }

        /// <summary>
        /// Gets or sets the number of active connections.
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// Gets or sets the number of requests per second.
        /// </summary>
        public int RequestsPerSecond { get; set; }

        /// <summary>
        /// Gets or sets the average response time in milliseconds.
        /// </summary>
        public float AverageResponseTime_Ms { get; set; }
    }
}