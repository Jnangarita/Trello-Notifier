using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using TrelloNotifier.Services;

namespace TrelloNotifier;

public partial class App : Application
{
    private Window? _mainWindow;
    private readonly IDisposable _sessionContext;
    private bool _closing;
    private bool _closed;
    private static int _fatalError;

    internal IntPtr WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(
        _mainWindow ?? throw new InvalidOperationException("La ventana principal todavía no está disponible."));

    public App()
    {
        AppLog.Initialize(debug: string.Equals(
            Environment.GetEnvironmentVariable("TRELLO_NOTIFIER_LOG_LEVEL"), "Debug", StringComparison.OrdinalIgnoreCase));
        _sessionContext = LogContext.PushProperty("SessionId", AppLog.SessionId);
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Log.ForContext<App>().Information(@"Aplicación iniciada.

  _______       _ _         _   _       _   _  __ _
 |__   __|     | | |       | \ | |     | | (_)/ _(_)
    | |_ __ ___| | | ___   |  \| | ___ | |_ _| |_ _  ___ _ __
    | | '__/ _ \ | |/ _ \  | . ` |/ _ \| __| |  _| |/ _ \ '__|
    | | | |  __/ | | (_) | | |\  | (_) | |_| | | | |  __/ |
    |_|_|  \___|_|_|\___/  |_| \_|\___/ \__|_|_| |_|\___|_|

Trello Notifier v{AppVersion}
Powered by .NET {RuntimeVersion} | WinUI 3 | Serilog
",
            typeof(App).Assembly.GetName().Version?.ToString(3), Environment.Version.ToString());
        InitializeComponent();
        AppServices.Notifications.Initialize();
    }

    public static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.AppWindow.Closing += OnWindowClosing;

        AppActivationArguments activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activationArgs.Kind == ExtendedActivationKind.AppNotification &&
            activationArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
        {
            AppServices.Notifications.ProcessActivation(notificationArgs);
        }

        _mainWindow.Activate();
        AppServices.Monitor.Start();
        if (AppLog.HasWriteFailure)
        {
            MainPage.Current?.ShowMessage("No se pudieron iniciar los logs. Revisa los permisos y el espacio disponible.",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        }
    }

    private async void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closed) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            await AppServices.Monitor.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.WriteFailure("StopMonitorOnClose", ex);
        }
        finally
        {
            CloseServicesAndLog();
            _mainWindow?.Close();
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => CloseServicesAndLog(orderly: false);

    private void CloseServicesAndLog(bool orderly = true)
    {
        if (_closed) return;
        _closed = true;
        try
        {
            AppServices.Monitor.Dispose();
            AppServices.Notifications.Dispose();
            if (orderly && Volatile.Read(ref _fatalError) == 0)
                Log.ForContext<App>().Information("Aplicación cerrada");
        }
        catch (Exception ex)
        {
            AppLog.WriteFailure("CloseApplication", ex);
        }
        finally
        {
            Log.CloseAndFlush();
            _sessionContext.Dispose();
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Interlocked.Exchange(ref _fatalError, 1);
        AppLog.WriteFailure("UnhandledUI", e.Exception, LogEventLevel.Fatal);
        Log.CloseAndFlush();
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Interlocked.Exchange(ref _fatalError, 1);
        if (e.ExceptionObject is Exception exception)
        {
            AppLog.WriteFailure("UnhandledProcess", exception, LogEventLevel.Fatal);
        }
        Log.CloseAndFlush();
    }
}
