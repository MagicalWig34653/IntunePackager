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
            for (var i = 0; i + 1 < e.Args.Length; i++)
            {
                // Only for tests and support: --language de|en and --settings-dir <folder>.
                if (e.Args[i] == "--language")
                {
                    language = e.Args[i + 1];
                }
                else if (e.Args[i] == "--settings-dir")
                {
                    settingsDirectory = e.Args[i + 1];
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
            var window = new MainWindow(new MainViewModel(services));
            MainWindow = window;
            window.Show();
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            Log.Log(LogLevel.Error, "Unhandled exception (terminating: " + args.IsTerminating + ")", args.ExceptionObject as Exception);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            // Only log here; the exception stays unhandled so the failure is never hidden.
            Log.Log(LogLevel.Error, "Unhandled UI exception", args.Exception);
        }
    }
}
