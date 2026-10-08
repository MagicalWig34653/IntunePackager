using System;
using System.Windows;
using System.Windows.Threading;
using IntunePackageBuilder.Core;
using IntunePackageBuilder.Core.Logging;

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
