using System;
using System.Collections.Generic;
using System.IO;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Sources;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>
    /// The one way from chosen paths to an analyzed installer: a drop, the file picker and the update flow all end here, so
    /// they check the same way (SPEC 5.1, A04).
    /// </summary>
    internal static class SourceSelector
    {
        /// <summary>
        /// Returns the analysis, or null with a localized <paramref name="message"/> (null message: the user cancelled).
        /// A folder needs advanced mode and the choice of the installer inside it.
        /// </summary>
        public static SourceAnalysis Analyze(AppServices services, bool advancedMode, IReadOnlyList<string> paths, out string message)
        {
            message = null;
            try
            {
                var item = SourceImporter.Classify(paths, advancedMode);
                string installer = null;
                if (item.Kind == DroppedKind.Folder)
                {
                    installer = AskForInstallerInFolder(services, item.Path, out message);
                    if (installer == null)
                    {
                        return null;
                    }
                }

                return SourceAnalyzer.Analyze(item, installer);
            }
            catch (ImportRejectedException exception)
            {
                message = Texts.ForImport(exception);
                return null;
            }
            catch (AnalysisFailedException exception)
            {
                services.Logger.Log(LogLevel.Warning, "The installer could not be analyzed", exception);
                message = Texts.ForAnalysis(exception);
                return null;
            }
        }

        private static string AskForInstallerInFolder(AppServices services, string folder, out string message)
        {
            message = null;
            var chosen = services.Dialogs.PickFile(Loc.Get("Dialog_InstallerFilter").Split('|')[0], Loc.Get("Dialog_InstallerFilter"), folder);
            if (chosen == null)
            {
                return null;
            }

            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(chosen);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                message = Loc.Get("Import_InstallerOutsideSource");
                return null;
            }

            return full.Substring(root.Length);
        }
    }
}
