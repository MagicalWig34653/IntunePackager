using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Analysis.Exe;
using IntunePackageBuilder.Analysis.Msi;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Build.Workflow
{
    /// <summary>Hints the user interface shows next to the analysis result. Codes only; the texts come from resources.</summary>
    public enum AnalysisNote
    {
        /// <summary>The MSI needs files of its source folder; a single file is not enough.</summary>
        MsiNeedsSourceFolder,

        /// <summary>The MSI has cabinets next to it.</summary>
        MsiExternalCabinets,

        /// <summary>The MSI does not set ALLUSERS, so it installs per user by default (open decision, docs/PLANUNG.md section 8).</summary>
        MsiPerUserDefault,

        /// <summary>The EXE carries no version information; the user enters name, manufacturer and version.</summary>
        ExeNoVersionInfo,

        /// <summary>For an EXE the silent parameters, the uninstall program and the detection file come from the vendor; nothing is guessed.</summary>
        ExeNeedsVendorInput
    }

    public enum AnalysisProblem
    {
        /// <summary>The installer is neither an MSI nor an EXE.</summary>
        UnsupportedFileType,

        /// <summary>The installer does not exist.</summary>
        FileMissing,

        /// <summary>The file is not an installer database or executable.</summary>
        NotAnInstaller,

        /// <summary>No installer was chosen inside a dropped folder.</summary>
        InstallerNotSelected,

        /// <summary>The chosen installer is not inside the dropped folder.</summary>
        InstallerOutsideFolder,

        /// <summary>The file could not be read.</summary>
        Unreadable
    }

    /// <summary>The analysis of a dropped installer failed. Nothing was changed.</summary>
    public sealed class AnalysisFailedException : Exception
    {
        public AnalysisFailedException(AnalysisProblem problem, string path, Exception inner = null)
            : base(problem + ": " + path, inner)
        {
            Problem = problem;
            Path = path;
        }

        public AnalysisProblem Problem { get; private set; }

        public string Path { get; private set; }
    }

    /// <summary>What reading an installer yielded: suggestions for the form and hints, never a final decision.</summary>
    public sealed class SourceAnalysis
    {
        public DroppedItem Item { get; set; }

        public InstallerType InstallerType { get; set; }

        public ImportKind ImportKind { get; set; }

        /// <summary>Path of the installer relative to the stored source (the file name for a single file).</summary>
        public string InstallerRelativePath { get; set; }

        public string SuggestedName { get; set; }

        public string SuggestedManufacturer { get; set; }

        public string SuggestedVersion { get; set; }

        /// <summary>MSI only.</summary>
        public string ProductCode { get; set; }

        public IReadOnlyList<AnalysisNote> Notes { get; set; }

        public MsiMetadata Msi { get; set; }

        public ExeMetadata Exe { get; set; }

        /// <summary>
        /// A draft configuration from the suggestions. Values the tool cannot know (EXE switches, uninstall program,
        /// detection file) stay empty and are reported by the validator; they are never filled with guesses.
        /// </summary>
        public PackageVersionConfig CreateConfiguration()
        {
            var config = PackageVersionConfig.CreateDefault(null, InstallerType);
            config.Identity.SoftwareName = SuggestedName;
            config.Identity.Manufacturer = SuggestedManufacturer;
            config.Identity.TargetVersion = SuggestedVersion;
            config.Source.InstallerRelativePath = InstallerRelativePath;
            config.Source.ImportKind = ImportKind;
            config.Detection.MinimumVersion = SuggestedVersion;
            if (InstallerType == InstallerType.Msi)
            {
                config.Install.ProductCode = ProductCode;
            }

            return config;
        }
    }

    /// <summary>Reads a dropped installer without running it (MSI database via the Windows Installer API, EXE version resource).</summary>
    public static class SourceAnalyzer
    {
        /// <summary>
        /// Analyzes the dropped item. A single file is the installer itself; for a folder the installer inside it
        /// must be named (advanced mode).
        /// </summary>
        /// <exception cref="AnalysisFailedException">The installer cannot be analyzed.</exception>
        public static SourceAnalysis Analyze(DroppedItem item, string installerRelativePath = null)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            string installerPath;
            string relative;
            ImportKind kind;
            if (item.Kind == DroppedKind.Folder)
            {
                if (string.IsNullOrWhiteSpace(installerRelativePath))
                {
                    throw new AnalysisFailedException(AnalysisProblem.InstallerNotSelected, item.Path);
                }

                relative = installerRelativePath.Trim().Replace('\\', '/');
                var segments = relative.Split('/');
                if (relative.StartsWith("/", StringComparison.Ordinal) || relative.IndexOf(':') >= 0 || segments.Any(s => s.Length == 0 || s == "." || s == ".."))
                {
                    throw new AnalysisFailedException(AnalysisProblem.InstallerOutsideFolder, relative);
                }

                installerPath = Path.Combine(item.Path, relative.Replace('/', Path.DirectorySeparatorChar));
                kind = ImportKind.Folder;
            }
            else
            {
                installerPath = item.Path;
                relative = Path.GetFileName(item.Path);
                kind = ImportKind.SingleFile;
            }

            if (!File.Exists(installerPath))
            {
                throw new AnalysisFailedException(AnalysisProblem.FileMissing, installerPath);
            }

            var extension = Path.GetExtension(installerPath);
            SourceAnalysis analysis;
            try
            {
                if (string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
                {
                    analysis = FromMsi(MsiReader.Read(installerPath));
                }
                else if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    analysis = FromExe(ExeReader.Read(installerPath));
                }
                else
                {
                    throw new AnalysisFailedException(AnalysisProblem.UnsupportedFileType, installerPath);
                }
            }
            catch (MsiReadException exception)
            {
                throw new AnalysisFailedException(
                    exception.Problem == MsiReadProblem.NotAnInstallerDatabase || exception.Problem == MsiReadProblem.MissingProductCode || exception.Problem == MsiReadProblem.InvalidProductCode || exception.Problem == MsiReadProblem.MissingProductVersion
                        ? AnalysisProblem.NotAnInstaller
                        : AnalysisProblem.Unreadable,
                    installerPath,
                    exception);
            }
            catch (ExeReadException exception)
            {
                throw new AnalysisFailedException(
                    exception.Problem == ExeReadProblem.NotAnExecutable ? AnalysisProblem.NotAnInstaller : AnalysisProblem.Unreadable,
                    installerPath,
                    exception);
            }

            analysis.Item = item;
            analysis.InstallerRelativePath = relative;
            analysis.ImportKind = kind;
            return analysis;
        }

        internal static SourceAnalysis FromMsi(MsiMetadata metadata)
        {
            var notes = new List<AnalysisNote>();
            if (metadata.RequiresSourceFolder)
            {
                notes.Add(AnalysisNote.MsiNeedsSourceFolder);
            }

            if (metadata.ExternalCabinets.Count > 0)
            {
                notes.Add(AnalysisNote.MsiExternalCabinets);
            }

            if (metadata.AllUsers == null)
            {
                notes.Add(AnalysisNote.MsiPerUserDefault);
            }

            return new SourceAnalysis
            {
                InstallerType = InstallerType.Msi,
                SuggestedName = metadata.ProductName,
                SuggestedManufacturer = metadata.Manufacturer,
                SuggestedVersion = metadata.ProductVersion,
                ProductCode = metadata.ProductCode,
                Notes = notes,
                Msi = metadata
            };
        }

        internal static SourceAnalysis FromExe(ExeMetadata metadata)
        {
            var notes = new List<AnalysisNote>();
            if (!metadata.HasVersionInfo)
            {
                notes.Add(AnalysisNote.ExeNoVersionInfo);
            }

            notes.Add(AnalysisNote.ExeNeedsVendorInput);
            return new SourceAnalysis
            {
                InstallerType = InstallerType.Exe,
                SuggestedName = metadata.SuggestedSoftwareName,
                SuggestedManufacturer = metadata.SuggestedManufacturer,
                SuggestedVersion = metadata.SuggestedVersion,
                Notes = notes,
                Exe = metadata
            };
        }
    }
}
