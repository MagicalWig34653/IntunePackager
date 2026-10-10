using System;
using System.Collections.Generic;
using IntunePackageBuilder.Build.Psadt;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Build.Pipeline
{
    /// <summary>The steps of a build in the order they run (SPEC section 7.2).</summary>
    public enum BuildPhase
    {
        ValidateConfiguration,
        AcquireLock,
        VerifySource,
        PrepareWorkspace,
        StagePackage,
        GenerateArtifacts,
        PackContent,
        VerifyPackage,
        Publish,
        Cleanup
    }

    public enum BuildProblem
    {
        /// <summary>The request itself is incomplete (a required folder is missing).</summary>
        RequestInvalid,

        /// <summary>The configuration is incomplete or invalid; the issues are in the detail.</summary>
        ConfigurationInvalid,

        /// <summary>Another process builds or changes this version.</summary>
        VersionLocked,

        /// <summary>The build folder cannot be created or written.</summary>
        NotWritable,

        /// <summary>There is not enough free disk space.</summary>
        NotEnoughSpace,

        /// <summary>The stored source or its manifest is missing, or the installer is not part of it.</summary>
        SourceMissing,

        /// <summary>The stored source differs from its manifest.</summary>
        SourceChanged,

        /// <summary>The runtime templates (entry script) are missing.</summary>
        TemplateMissing,

        /// <summary>A path in the package would be longer than Windows allows.</summary>
        PathTooLong,

        /// <summary>The configuration selects the PSAppDeployToolkit but no toolkit ZIP is configured or the file is gone.</summary>
        ToolkitMissing,

        /// <summary>The toolkit ZIP is not one of the pinned versions and was not confirmed.</summary>
        ToolkitNotRecognized,

        /// <summary>The toolkit ZIP is unreadable, incomplete or unsafe.</summary>
        ToolkitInvalid,

        /// <summary>A branding image of the configuration is missing in the version folder.</summary>
        ToolkitAssetsMissing,

        /// <summary>The packaging tool failed; the inner exception is a <see cref="IntunePackageBuilder.Build.Packaging.ContentPrepException"/>.</summary>
        PackagingFailed,

        /// <summary>The result could not be published into the build folder.</summary>
        PublishFailed,

        Cancelled,

        Unexpected
    }

    public sealed class BuildProgress
    {
        public BuildProgress(BuildPhase phase, TimeSpan elapsed)
        {
            Phase = phase;
            Elapsed = elapsed;
        }

        public BuildPhase Phase { get; private set; }

        /// <summary>Time since the build started. No percentage is reported: none of the steps can be measured reliably.</summary>
        public TimeSpan Elapsed { get; private set; }
    }

    /// <summary>A build did not finish. Nothing is published; the log has the details. Carries codes, not display text.</summary>
    public sealed class BuildFailedException : Exception
    {
        public BuildFailedException(BuildPhase phase, BuildProblem problem, string detail, Exception inner = null)
            : base(problem + " in phase " + phase + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail), inner)
        {
            Phase = phase;
            Problem = problem;
            Detail = detail ?? string.Empty;
        }

        public BuildPhase Phase { get; private set; }

        public BuildProblem Problem { get; private set; }

        public string Detail { get; private set; }

        /// <summary>Build ID of the failed build, when it was already assigned.</summary>
        public string BuildId { get; set; }
    }

    public sealed class BuildRequest
    {
        public BuildRequest()
        {
            ToolTimeout = TimeSpan.FromMinutes(30);
            Language = "en";
        }

        /// <summary>Folder of the version (<c>versions/&lt;version&gt;</c>), with <c>source</c>, <c>source-manifest.json</c> and <c>builds</c>.</summary>
        public string VersionDirectory { get; set; }

        /// <summary>Configuration to build. A private copy goes into the snapshot; later edits do not change the build.</summary>
        public PackageVersionConfig Configuration { get; set; }

        /// <summary><c>de</c> or <c>en</c>: language of the generated texts, recorded in the snapshot.</summary>
        public string Language { get; set; }

        /// <summary>Folder with the client runtime templates (<c>Install.cmd</c> and helpers) copied into the package root.</summary>
        public string RuntimeTemplateDirectory { get; set; }

        public string ContentPrepToolPath { get; set; }

        public bool AllowUnknownTool { get; set; }

        /// <summary>What the PSAppDeployToolkit engine needs. Required when the configuration selects that engine, ignored otherwise.</summary>
        public PsadtSupply Psadt { get; set; }

        public TimeSpan ToolTimeout { get; set; }

        /// <summary>Short, writable folder for the temporary working folder of the build (default: below the temp folder).</summary>
        public string WorkRoot { get; set; }

        public IntuneSettingsOptions IntuneOptions { get; set; }

        /// <summary>Returns the free bytes of the volume of a path. Replaceable for tests; the default asks the drive.</summary>
        public Func<string, long> FreeSpaceProbe { get; set; }
    }

    public sealed class BuildResult
    {
        public string BuildId { get; set; }

        /// <summary>Published build folder (<c>builds/&lt;build-id&gt;</c>).</summary>
        public string BuildDirectory { get; set; }

        public string PackagePath { get; set; }

        public string IntuneDirectory { get; set; }

        public IReadOnlyList<string> Files { get; set; }
    }
}
