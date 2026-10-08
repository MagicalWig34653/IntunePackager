using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.Core.Settings
{
    /// <summary>The loaded settings (never null) and the reason they are only defaults, if any.</summary>
    public sealed class SettingsLoadResult
    {
        public SettingsLoadResult(AppSettings settings, Exception error)
        {
            Settings = settings;
            Error = error;
        }

        public AppSettings Settings { get; private set; }

        /// <summary>Null when the file was read (or does not exist); otherwise why defaults are used.</summary>
        public Exception Error { get; private set; }
    }

    /// <summary>
    /// Reads and writes <c>settings.json</c> in a directory the user can write to. A missing, damaged or
    /// newer settings file never prevents the program from starting: <see cref="Load"/> then returns defaults
    /// plus the error. A damaged file is kept as a backup before it is replaced; a newer one is never replaced.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly string _file;
        private readonly int _currentSchemaVersion;
        private readonly IReadOnlyList<IMigrationStep> _steps;

        public SettingsStore(string directory)
            : this(directory, AppSettings.CurrentSchemaVersion, new IMigrationStep[0])
        {
        }

        internal SettingsStore(string directory, int currentSchemaVersion, IReadOnlyList<IMigrationStep> steps)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A settings directory is required.", "directory");
            }

            _file = Path.Combine(directory, AppSettings.FileName);
            _currentSchemaVersion = currentSchemaVersion;
            _steps = steps;
        }

        public string FilePath
        {
            get { return _file; }
        }

        /// <summary>Default location: %LOCALAPPDATA%\Intune Package Builder.</summary>
        public static string DefaultDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ProductName);
        }

        public SettingsLoadResult Load()
        {
            if (!File.Exists(_file))
            {
                return new SettingsLoadResult(NewDefaults(), null);
            }

            try
            {
                var settings = VersionedJsonFile.Read<AppSettings>(_file, _currentSchemaVersion, _steps, loaded => loaded.Normalize());
                return new SettingsLoadResult(settings, null);
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                return new SettingsLoadResult(NewDefaults(), ex);
            }
        }

        /// <summary>Saves the settings atomically.</summary>
        /// <exception cref="UnsupportedSchemaException">The existing file was written by a newer program.</exception>
        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (File.Exists(_file))
            {
                var existing = Load();
                if (existing.Error is UnsupportedSchemaException)
                {
                    throw existing.Error;
                }

                if (existing.Error is StorageFormatException)
                {
                    PreserveDamagedFile();
                }
            }

            settings.SchemaVersion = _currentSchemaVersion;
            VersionedJsonFile.Write(_file, settings);
        }

        private AppSettings NewDefaults()
        {
            return new AppSettings { SchemaVersion = _currentSchemaVersion };
        }

        private void PreserveDamagedFile()
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Copy(_file, _file + ".invalid-" + stamp + ".bak", true);
        }

        private static bool IsReadFailure(Exception ex)
        {
            return ex is StorageFormatException
                || ex is UnsupportedSchemaException
                || ex is MigrationException
                || ex is IOException
                || ex is UnauthorizedAccessException;
        }
    }
}
