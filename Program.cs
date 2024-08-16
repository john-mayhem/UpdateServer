using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Linq;
using UpdateServer.Controllers;
using UpdateServer.Services;

namespace UpdateServer
{
    /// <summary>
    /// The main entry point for the Update Server application.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main method that initializes and runs the application.
        /// </summary>
        /// <param name="args">Command line arguments passed to the application.</param>
        public static async Task Main(string[] args)
        {
            // ASCII art header for the application
            Console.WriteLine(@"
╔═══════════════════════════════════════════╗
║        Update Server Application          ║
║              Version 0.3                  ║
╚═══════════════════════════════════════════╝");

            // Configure and create the logger
            Log.Logger = CreateLoggerConfiguration().CreateLogger();

            try
            {
                Log.Information("Starting Update Server application");
                var host = CreateHostBuilder(args).Build();

                using (var scope = host.Services.CreateScope())
                {
                    // Initialize the database based on command line arguments
                    await InitializeDatabaseAsync(scope, args);
                }

                Log.Information("Initializing web host");
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.Information("Shutting down Update Server application");
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Creates and configures the logger for the application.
        /// </summary>
        /// <returns>A configured LoggerConfiguration object.</returns>
        private static LoggerConfiguration CreateLoggerConfiguration()
        {
            // Create a logger that writes to both console and file
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}",
                    theme: AnsiConsoleTheme.Code)
                .WriteTo.File($"logs/log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        /// <summary>
        /// Initializes the database based on the provided command line arguments.
        /// </summary>
        /// <param name="scope">The dependency injection scope.</param>
        /// <param name="args">Command line arguments.</param>
        private static async Task InitializeDatabaseAsync(IServiceScope scope, string[] args)
        {
            // Get required services from the dependency injection container
            var services = scope.ServiceProvider;
            var dbController = services.GetRequiredService<DbController>();
            var dbInstall = services.GetRequiredService<DbInstall>();
            var environment = services.GetRequiredService<IHostEnvironment>();
            var configuration = services.GetRequiredService<IConfiguration>();

            var dbLogger = new DbLogger(configuration, environment);

            if (args.Contains("-installdb"))
            {
                // Install the database if the -installdb argument is provided
                await InstallDatabaseAsync(dbInstall);
            }
            else
            {
                // Otherwise, check and initialize the existing database
                await CheckAndInitializeDatabaseAsync(dbController);
            }

            // Process any pending log entries from the previous run
            Log.Information("Processing log entries from previous session");
            await dbLogger.ProcessLastLogFileAsync();
        }

        /// <summary>
        /// Installs the database using the provided DbInstall service.
        /// </summary>
        /// <param name="dbInstall">The DbInstall service.</param>
        private static async Task InstallDatabaseAsync(DbInstall dbInstall)
        {
            Log.Information("Beginning database installation process");
            if (await dbInstall.InstallDbAsync())
            {
                Log.Information("Database installation completed successfully");
            }
            else
            {
                Log.Fatal("Database installation failed. The application cannot continue");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Checks and initializes the database using the provided DbController.
        /// </summary>
        /// <param name="dbController">The DbController service.</param>
        private static async Task CheckAndInitializeDatabaseAsync(DbController dbController)
        {
            Log.Information("Verifying database connection and structure");
            if (!await dbController.InitializeAsync())
            {
                LogDatabaseInitializationError();
                Environment.Exit(1);
            }

            if (!await dbController.CheckDatabaseStructureAsync())
            {
                LogDatabaseStructureError();
                Environment.Exit(1);
            }
            Log.Information("Database verification completed successfully");
        }

        /// <summary>
        /// Logs detailed information about database initialization errors.
        /// </summary>
        private static void LogDatabaseInitializationError()
        {
            Log.Fatal("Database initialization failed. The application cannot start");
            Log.Error("Possible reasons for failure:");
            Log.Error("1. Database server is not running or inaccessible");
            Log.Error("2. 'UpdateServer' database does not exist");
            Log.Error("3. Incorrect database credentials in configuration");
            Log.Information("Please check the following:");
            Log.Information("- Ensure SQL Server is running and accessible");
            Log.Information("- Verify connection string in appsettings.json");
            Log.Information("- For first-time setup, use: dotnet run -- -installdb");
            Log.Information("Check logs for specific error details");
        }

        /// <summary>
        /// Logs detailed information about database structure errors.
        /// </summary>
        private static void LogDatabaseStructureError()
        {
            Log.Fatal("Database structure is incorrect or incomplete");
            Log.Error("Possible reasons:");
            Log.Error("1. Manual database creation without required tables");
            Log.Error("2. Interrupted or failed previous installation");
            Log.Information("To resolve:");
            Log.Information("Run: dotnet run -- -installdb");
            Log.Warning("Warning: This will reset the database and delete existing data");
            Log.Information("For data preservation, consult documentation or support");
        }

        /// <summary>
        /// Creates and configures the host builder for the application.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>A configured IHostBuilder object.</returns>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog() // Use Serilog for logging
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    // Configure application settings
                    webBuilder.ConfigureAppConfiguration((context, config) =>
                    {
                        config.Sources.Clear();
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                        config.AddEnvironmentVariables();
                    });
                });
    }
}