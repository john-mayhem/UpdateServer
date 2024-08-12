using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
using System.Text;
using Serilog;
using Microsoft.AspNetCore.Authorization;
using AspNetCoreRateLimit;
using UpdateServer.Controllers;
using UpdateServer.Services;
using UpdateServer.Middleware;

namespace UpdateServer
{
    /// <summary>
    /// Configures the application's services and request pipeline.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration">The application's configuration.</param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            Log.Information("Startup initialized");
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configures the application's services.
        /// </summary>
        /// <param name="services">The collection of services to configure.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            Log.Information("Configuring services");

            ConfigureRateLimiting(services);
            ConfigureFrameworkServices(services);
            ConfigureAuthentication(services);
            ConfigureAuthorization(services);

            Log.Information("Services configuration completed");
        }

        /// <summary>
        /// Configures the rate limiting services.
        /// </summary>
        private void ConfigureRateLimiting(IServiceCollection services)
        {
            Log.Debug("Configuring rate limiting");
            services.AddMemoryCache();
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
            services.AddInMemoryRateLimiting();
            services.Configure<ClientRateLimitOptions>(Configuration.GetSection("ClientRateLimiting"));
            services.AddSingleton<IClientPolicyStore, MemoryCacheClientPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        }

        /// <summary>
        /// Configures the framework services.
        /// </summary>
        private static void ConfigureFrameworkServices(IServiceCollection services)
        {
            Log.Debug("Configuring framework services");
            services.AddControllers();
            services.AddSingleton<DbController>();
            services.AddSingleton<DbInstall>();
            services.AddSingleton<DbLogger>();
            services.AddSingleton<DbUserValidation>();
            services.AddSingleton<FileStorageService>();
            services.AddSingleton<PerformanceService>();
            services.AddHostedService<PerformanceService>();
        }

        /// <summary>
        /// Configures the authentication services.
        /// </summary>
        private void ConfigureAuthentication(IServiceCollection services)
        {
            Log.Debug("Configuring authentication");
            var jwtSecret = Configuration["Jwt:Secret"];
            if (string.IsNullOrEmpty(jwtSecret))
            {
                Log.Error("JWT secret is not configured");
                throw new InvalidOperationException("JWT secret is not configured.");
            }

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = Configuration["Jwt:Issuer"],
                        ValidAudience = Configuration["Jwt:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
                    };
                });
        }

        /// <summary>
        /// Configures the authorization services.
        /// </summary>
        private static void ConfigureAuthorization(IServiceCollection services)
        {
            Log.Debug("Configuring authorization");
            services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());
        }

        /// <summary>
        /// Configures the application's request pipeline.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="env">The hosting environment.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            Log.Information("Configuring application");

            if (env.IsDevelopment())
            {
                Log.Debug("Development environment detected, using developer exception page");
                app.UseDeveloperExceptionPage();
            }

            app.UseIpRateLimiting();
            Log.Debug("IP rate limiting configured");

            app.UseSerilogRequestLogging();
            Log.Debug("Serilog request logging configured");

            app.UseRouting();
            Log.Debug("Routing configured");

            app.UseMiddleware<LogEnrichmentMiddleware>();
            Log.Debug("Log enrichment middleware configured");

            app.UseClientRateLimiting();
            Log.Debug("Client rate limiting configured");

            app.UseAuthentication();
            Log.Debug("Authentication configured");

            app.UseAuthorization();
            Log.Debug("Authorization configured");

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                Log.Debug("Controller endpoints mapped");
            });

            Log.Information("Application configuration completed");
        }
    }
}