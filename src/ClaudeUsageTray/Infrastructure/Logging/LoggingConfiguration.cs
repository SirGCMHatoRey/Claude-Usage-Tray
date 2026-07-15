using System.IO;
using Serilog;
using Serilog.Events;

namespace ClaudeUsageTray.Infrastructure.Logging;

public static class LoggingConfiguration
{
    public static LoggerConfiguration CreateDefault()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeUsageTray", "logs", "app-.log");

        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
            .Enrich.WithThreadId()
            .Enrich.WithProperty("App", "ClaudeUsageTray")
            .WriteTo.Debug(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] (T{ThreadId}) {Message:lj}{NewLine}{Exception}",
                shared: false);
    }
}
