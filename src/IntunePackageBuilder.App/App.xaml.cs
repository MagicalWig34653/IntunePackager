using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Core;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Settings;

namespace IntunePackageBuilder.App
{
    public partial class App : Application
    {
        internal static ILogger Log { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Set up logging first so failures before the first window are recorded (SPEC section 10).
            Log = new FileLogger(FileLogger.DefaultDirectory(), "app");
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            Log.Log(LogLevel.Info, AppInfo.ProductName + " " + AppInfo.Version + " starting");
            base.OnStartup(e);

            string language = null;
            string settingsDirectory = null;
            string openPath = null;
            for (var i = 0; i + 1 < e.Args.Length; i++)
            {
                // Only for tests and support: --language de|en, --settings-dir <folder> and --open <installer> (the same check as a drop).
                if (e.Args[i] == "--language")
                {
                    language = e.Args[i + 1];
                }
                else if (e.Args[i] == "--settings-dir")
                {
                    settingsDirectory = e.Args[i + 1];
                }
                else if (e.Args[i] == "--open")
                {
                    openPath = e.Args[i + 1];
                }
            }

            // The program language follows the UI language of Windows unless one is given.
            Loc.Use(language == null ? CultureInfo.CurrentUICulture : new CultureInfo(language));

            var services = new AppServices(
                new SettingsStore(settingsDirectory ?? SettingsStore.DefaultDirectory()),
                new DialogsImpl(),
                new ShellImpl(Log),
                new ContentPrepTool(),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "template"));
            services.Logger = Log;
            var model = new MainViewModel(services);
            var window = new MainWindow(model);
            MainWindow = window;
            window.Show();
            if (openPath != null)
            {
                model.Start.HandleDrop(new[] { openPath });
            }
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Log.Log(LogLevel.Error, "Unhandled exception (terminating: " + args.IsTerminating + ")", args.ExceptionObject as Exception);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            // The failure is logged and shown in plain words instead of the operating system's crash dialog (SPEC 10).
            Log.Log(LogLevel.Error, "Unhandled UI exception", args.Exception);
            try
            {
                MessageBox.Show(
                    Loc.Format("Dialog_UnexpectedError", FileLogger.DefaultDirectory()),
                    Loc.Get("Dialog_UnexpectedErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            }
            catch (InvalidOperationException)
            {
                // No dialog could be shown (for example during shutdown); the exception then stays unhandled.
                Log.Log(LogLevel.Warning, "The error dialog could not be shown");
            }
        }
    }
}
