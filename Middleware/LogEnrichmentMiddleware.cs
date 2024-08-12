using Serilog.Context;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace UpdateServer.Middleware
{
    /// <summary>
    /// Middleware to enrich log entries with user and IP address information.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="LogEnrichmentMiddleware"/> class.
    /// </remarks>
    /// <param name="next">The next middleware in the pipeline.</param>
    public class LogEnrichmentMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;

        /// <summary>
        /// Processes the request and adds enrichment properties to the log context.
        /// </summary>
        /// <param name="context">The HTTP context for the current request.</param>
        public async Task Invoke(HttpContext context)
        {
            // Extract username from the user identity, or use "Anonymous" if not available
            var username = context.User.Identity?.Name ?? "Anonymous";
            
            // Extract IP address from the connection, or use "Unknown" if not available
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            // Push properties to the LogContext for the duration of the request
            using (LogContext.PushProperty("Username", username))
            using (LogContext.PushProperty("IPAddress", ipAddress))
            {
                // Call the next middleware in the pipeline
                await _next(context);
            }
        }
    }
}