using ClaudeUsageTray.Application.State;
using ClaudeUsageTray.Application.Sync;
using ClaudeUsageTray.Infrastructure.Cache;
using ClaudeUsageTray.Infrastructure.ClaudeAi;
using ClaudeUsageTray.Infrastructure.Logging;
using ClaudeUsageTray.Infrastructure.Security;
using ClaudeUsageTray.Presentation.Settings;
using ClaudeUsageTray.Presentation.Tray;
using ClaudeUsageTray.Presentation.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Windows;

namespace ClaudeUsageTray;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayController? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = LoggingConfiguration.CreateDefault().CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(RegisterServices)
            .Build();

        await _host.StartAsync();

        var credentials = _host.Services.GetRequiredService<ICredentialStore>();
        if (!credentials.HasSessionKey())
        {
            var settings = _host.Services.GetRequiredService<SettingsWindow>();
            settings.ShowDialog();
        }

        _tray = _host.Services.GetRequiredService<TrayController>();

        var widget = _host.Services.GetRequiredService<FloatingWidget>();
        widget.Show();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IUsageCache, UsageCache>();

        services.AddSingleton<IWebViewApiGateway, WebViewApiGateway>();
        services.AddSingleton<IClaudeAiUsageProvider, ClaudeAiUsageProvider>();

        services.AddSingleton<IUsageStore, UsageStore>();
        services.AddSingleton<SyncOrchestrator>();
        services.AddHostedService(p => p.GetRequiredService<SyncOrchestrator>());

        services.AddSingleton<FloatingWidgetViewModel>();
        services.AddSingleton<FloatingWidget>();
        services.AddSingleton<SettingsWindow>();
        services.AddSingleton<TrayController>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
