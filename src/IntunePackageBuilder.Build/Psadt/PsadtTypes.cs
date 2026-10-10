using System;
using System.Collections.Generic;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json.Linq;

namespace IntunePackageBuilder.Build.Psadt
{
    public enum PsadtProblem
    {
        /// <summary>No toolkit ZIP is configured or the file does not exist.</summary>
        PackageMissing,

        /// <summary>The file is not a readable ZIP.</summary>
        PackageUnreadable,

        /// <summary>The SHA-256 of the ZIP is not one of the pinned versions and the user did not confirm it.</summary>
        PackageNotRecognized,

        /// <summary>The ZIP lacks files of the toolkit module.</summary>
        PackageInvalid,

        /// <summary>The ZIP contains paths that leave the target folder, or is unreasonably large.</summary>
        PackageUnsafe,

        /// <summary>The pin file with the known versions is missing or unreadable.</summary>
        PinMissing,

        /// <summary>A branding image is missing, too large or not a PNG or JPEG file.</summary>
        ImageInvalid
    }

    /// <summary>Carries a code, not display text.</summary>
    public sealed class PsadtException : Exception
    {
        public PsadtException(PsadtProblem problem, string detail, Exception inner = null)
            : base(problem + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail), inner)
        {
            Problem = problem;
            Detail = detail ?? string.Empty;
        }

        public PsadtProblem Problem { get; private set; }

        public string Detail { get; private set; }
    }

    public sealed class PsadtPinVersion
    {
        public string Version { get; set; }

        public string Asset { get; set; }

        public string Sha256 { get; set; }

        public string Commit { get; set; }
    }

    /// <summary>The known toolkit versions from <c>psadt.json</c> (maintained by the update workflow, see docs/PLANUNG.md section 8, number 12).</summary>
    public static class PsadtPins
    {
        public const string FileName = "psadt.json";

        public static IReadOnlyList<PsadtPinVersion> Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                throw new PsadtException(PsadtProblem.PinMissing, path);
            }

            try
            {
                var document = JsonFormat.ParseObject(System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
                var result = new List<PsadtPinVersion>();
                var versions = document["versions"] as JArray;
                if (versions == null)
                {
                    throw new PsadtException(PsadtProblem.PinMissing, "no versions in " + path);
                }

                foreach (var item in versions)
                {
                    var version = (string)item["version"];
                    var sha = (string)item["sha256"];
                    if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(sha) || sha.Length != 64)
                    {
                        throw new PsadtException(PsadtProblem.PinMissing, "invalid entry in " + path);
                    }

                    result.Add(new PsadtPinVersion
                    {
                        Version = version,
                        Asset = (string)item["asset"],
                        Sha256 = sha.ToLowerInvariant(),
                        Commit = (string)item["commit"]
                    });
                }

                return result;
            }
            catch (Newtonsoft.Json.JsonException exception)
            {
                throw new PsadtException(PsadtProblem.PinMissing, path, exception);
            }
        }
    }

    public sealed class PsadtPackageInfo
    {
        /// <summary>Pinned version of the ZIP, or null when its SHA-256 is unknown and the user confirmed it.</summary>
        public string Version { get; set; }

        public string Sha256 { get; set; }

        /// <summary>Total size of the module files that go into a package.</summary>
        public long UncompressedBytes { get; set; }
    }

    /// <summary>What the build needs to produce a package with the toolkit: the templates, the ZIP the user supplied and the pins.</summary>
    public sealed class PsadtSupply
    {
        /// <summary>Folder with <c>Install.cmd</c> and <c>Invoke-AppDeployToolkit.ps1</c> of the toolkit engine.</summary>
        public string TemplateDirectory { get; set; }

        /// <summary><c>PSAppDeployToolkit_Template_v4.zip</c> of a release; supplied by the user, never shipped (docs/PLANUNG.md section 8, number 12).</summary>
        public string PackagePath { get; set; }

        /// <summary>The <c>psadt.json</c> with the known versions.</summary>
        public string PinPath { get; set; }

        /// <summary>Accepts a ZIP whose SHA-256 is not pinned. Only after an explicit confirmation of the user.</summary>
        public bool AllowUnknown { get; set; }
    }
}
