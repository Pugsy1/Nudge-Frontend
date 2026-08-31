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
using Nudge.Data.DependencyInjection;
using Nudge.Library.DependencyInjection;
using Nudge.Media.DependencyInjection;
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
            // wrong palette on start. Reading the settings file is small and quick; the genuinely
            // slow startup work is deliberately deferred until after the window is up (below).
            var themeService = _host.Services.GetRequiredService<IThemeService>();
            var uiStyleService = _host.Services.GetRequiredService<IUiStyleService>();
            var shadowEffectService = _host.Services.GetRequiredService<IShadowEffectService>();
            var settings = _host.Services.GetRequiredService<Core.Abstractions.ISettingsService>();
            Core.Models.NudgeSettings saved = await settings.LoadAsync().ConfigureAwait(true);
            themeService.Apply(themeService.Parse(saved.ThemeName));
            // After the palette, before anything is shown: the style overlay shadows radius/effect
            // keys, so applying it up front avoids the window rendering one style then restyling.
            uiStyleService.Apply(uiStyleService.Parse(saved.UiStyleName));
            shadowEffectService.Apply(saved.EnableShadows, saved.ShadowIntensityPercent);

            // Shown before database migration and discovery, not after: the splash is the whole
            // point of this ordering, and it can only actually animate if it is on screen while
            // that work happens rather than after it. Previously everything below ran first, so the
            // window appeared already-frozen mid-startup, then animated briefly, then vanished.
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            var splashClock = System.Diagnostics.Stopwatch.StartNew();

            // Task.Run, not a bare await: EF Core's migrator is largely synchronous under its async
            // surface, so awaiting it directly still blocks the UI thread - which stalls the splash's
            // own animations, the exact "freezes, then loads" behaviour this ordering exists to fix.
            // Nothing here touches UI state, so moving it off the UI thread is safe.
            await Task.Run(() => _host.Services.MigrateNudgeDatabaseAsync()).ConfigureAwait(true);

            // ShellViewModel.InitialiseAsync delegates to SetupViewModel and then, once an
            // installation is confirmed, switches the window to the library screen underneath.
            await _host.Services.GetRequiredService<ShellViewModel>()
                .InitialiseAsync()
                .ConfigureAwait(true);

            // A floor, not a fixed duration: on a warm start the work above can finish in a few
            // hundred milliseconds, and dismissing that fast reads as a flicker rather than a splash.
            const int MinimumSplashMilliseconds = 1100;
            int remaining = MinimumSplashMilliseconds - (int)splashClock.ElapsedMilliseconds;
            if (remaining > 0)
            {
                await Task.Delay(remaining).ConfigureAwait(true);
            }

            window.DismissSplash();
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

        // The database and the folder scanner, behind ITableRepository and IVpxLibraryScanner.
        // ITableRepository and NudgeDbContext are Scoped (EF Core's default); resolve them through
        // an IServiceScope, not directly from the root container - see LibraryViewModel.
        string dataDirectory = Vpx.DependencyInjection.ServiceCollectionExtensions.ResolveDataDirectory(environment);
        Directory.CreateDirectory(dataDirectory);
        string databasePath = Path.Combine(dataDirectory, Data.DependencyInjection.ServiceCollectionExtensions.DatabaseFileName);
        builder.Services.AddNudgeData(databasePath);
        builder.Services.AddNudgeLibrary();

        // The vps-db artwork lookup/cache, behind IArtworkProvider. Shares dataDirectory's parent
        // rather than the database path itself - it's its own subfolder (index + cached images), not
        // part of the SQLite file.
        string artworkCacheDirectory = Path.Combine(dataDirectory, "artwork");
        Directory.CreateDirectory(artworkCacheDirectory);
        builder.Services.AddNudgeMedia(artworkCacheDirectory);

        // Hand-picked covers live beside the scraped cache but in their own subfolder, so clearing
        // one never takes the other with it - a scraped image can always be re-fetched, whereas a
        // user's own picture is the only copy Nudge has once the original moves.
        string customArtworkDirectory = Path.Combine(artworkCacheDirectory, "custom");
        builder.Services.AddSingleton<ICustomArtworkStore>(sp =>
            new CustomArtworkStore(
                customArtworkDirectory,
                sp.GetRequiredService<ILogger<CustomArtworkStore>>()));

        // UI services.
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IUiStyleService, UiStyleService>();
        builder.Services.AddSingleton<IShadowEffectService, ShadowEffectService>();
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();

        builder.Services.AddSingleton<SetupViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static void ConfigureLogging(IEnvironmentPaths environment, IPathRedactor redactor)
    {
        string logsDirectory = Vpx.DependencyInjection.ServiceCollectionExtensions.ResolveLogsDirectory(environment);
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
