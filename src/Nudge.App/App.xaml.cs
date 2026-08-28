using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nudge.App.Logging;
using Nudge.App.Services;
using Nudge.App.ViewModels;
using Nudge.App.Views;
using Nudge.Core.Diagnostics;
using Nudge.Vpx.DependencyInjection;
using Nudge.Vpx.Platform;
using Serilog;
using Serilog.Events;

namespace Nudge.App;

/// <summary>
/// Application entry point. Builds the dependency injection container, configures logging, and shows
/// the setup window.
/// </summary>
public partial class App : Application
{
    private const string LogOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The redactor is needed before the container exists, because it configures the logger.
        var environment = new WindowsEnvironmentPaths();
        IPathRedactor redactor = new PathRedactor(environment.UserName, environment.UserProfile);

        ConfigureLogging(environment, redactor);

        Log.Information("Nudge starting up.");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _host = BuildHost(environment, redactor);
            await _host.StartAsync().ConfigureAwait(true);

            // Restore the saved theme before anything is shown, so the window never flashes the
            // wrong palette on start.
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            var settings = _host.Services.GetRequiredService<Core.Abstractions.ISettingsService>();
            Core.Models.NudgeSettings saved = await settings.LoadAsync().ConfigureAwait(true);
            themeService.Apply(themeService.Parse(saved.ThemeName));

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            // Discovery runs after the window is up, so the user sees something immediately.
            await _host.Services.GetRequiredService<SetupViewModel>()
                .InitialiseAsync()
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Nudge failed to start.");
            MessageBox.Show(
                "Nudge could not start. The log file in %LocalAppData%\\Nudge\\logs has the details.\n\n"
                + ex.Message,
                "Nudge",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("Nudge shutting down.");

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync().ConfigureAwait(false);
        base.OnExit(e);
    }

    private static IHost BuildHost(IEnvironmentPaths environment, IPathRedactor redactor)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        // These two are already built, so they are registered as the instances the logger uses.
        builder.Services.AddSingleton(environment);
        builder.Services.AddSingleton(redactor);

        // Everything that touches the filesystem, the registry or Visual Pinball.
        builder.Services.AddNudgeVpx();

        // UI services.
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();

        builder.Services.AddSingleton<SetupViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static void ConfigureLogging(IEnvironmentPaths environment, IPathRedactor redactor)
    {
        string logsDirectory = ServiceCollectionExtensions.ResolveLogsDirectory(environment);
        string logFilePath = Path.Combine(logsDirectory, "nudge-.log");

        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                new RedactingTextFormatter(LogOutputTemplate, redactor),
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                shared: true);

#if DEBUG
        configuration = configuration.WriteTo.Console(
            new RedactingTextFormatter(LogOutputTemplate, redactor));
#endif

        Log.Logger = configuration.CreateLogger();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unhandled error reached the UI thread.");

        MessageBox.Show(
            "Something went wrong. Nudge will keep running, but the action you tried did not "
            + "finish.\n\nThe log file in %LocalAppData%\\Nudge\\logs has the details.\n\n"
            + e.Exception.Message,
            "Nudge",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Handled so that one failed action does not take the whole application down.
        e.Handled = true;
    }

    private static void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "An unhandled error occurred outside the UI thread.");
        }

        Log.CloseAndFlush();
    }
}
