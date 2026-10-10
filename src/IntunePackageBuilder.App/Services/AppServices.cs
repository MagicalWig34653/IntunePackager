using System;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Settings;

namespace IntunePackageBuilder.App.Services
{
    /// <summary>Everything the view models need from the outside, in one place, so tests can replace it.</summary>
    public sealed class AppServices
    {
        public AppServices(SettingsStore settingsStore, IUserDialogs dialogs, IShell shell, IContentPrepRunner runner, string runtimeTemplateDirectory)
        {
            SettingsStore = settingsStore;
            Dialogs = dialogs;
            Shell = shell;
            Runner = runner;
            RuntimeTemplateDirectory = runtimeTemplateDirectory;
            Logger = new NullLogger();
            Clock = () => DateTime.UtcNow;
            var loaded = settingsStore.Load();
            Settings = loaded.Settings;
            SettingsError = loaded.Error;
        }

        public SettingsStore SettingsStore { get; private set; }

        /// <summary>The settings in use. Never null: unreadable settings give defaults and <see cref="SettingsError"/>.</summary>
        public AppSettings Settings { get; private set; }

        public Exception SettingsError { get; private set; }

        public IUserDialogs Dialogs { get; private set; }

        public IShell Shell { get; private set; }

        public IContentPrepRunner Runner { get; private set; }

        /// <summary>Folder with the client runtime templates (<c>Install.cmd</c> and helpers) shipped with the program.</summary>
        public string RuntimeTemplateDirectory { get; private set; }

        /// <summary>Folder with the entry script and <c>Install.cmd</c> of the PSAppDeployToolkit engine, shipped with the program.</summary>
        public string PsadtTemplateDirectory { get; set; }

        /// <summary>The <c>psadt.json</c> with the pinned toolkit versions, shipped with the program.</summary>
        public string PsadtPinPath { get; set; }

        /// <summary>Short working folder for builds; null uses the default below the temp folder.</summary>
        public string WorkRoot { get; set; }

        public ILogger Logger { get; set; }

        public Func<DateTime> Clock { get; set; }

        public BaseFolderResolution BaseFolder
        {
            get { return BaseFolderResolver.Resolve(Settings); }
        }

        public void SaveSettings()
        {
            try
            {
                SettingsStore.Save(Settings);
            }
            catch (Exception exception)
            {
                Logger.Log(LogLevel.Error, "The settings could not be saved", exception);
            }
        }
    }
}
