using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using IntunePackageBuilder.Build.Processes;

namespace IntunePackageBuilder.Build.Packaging
{
    public sealed class ContentPrepToolVersion
    {
        public ContentPrepToolVersion(string version, string sha256)
        {
            Version = version;
            Sha256 = sha256;
        }

        public string Version { get; private set; }

        public string Sha256 { get; private set; }
    }

    public sealed class PackRequest
    {
        public PackRequest()
        {
            Timeout = TimeSpan.FromMinutes(30);
        }

        /// <summary>Path to <c>IntuneWinAppUtil.exe</c>. The tool is not shipped with this program (docs/PLANUNG.md section 8).</summary>
        public string ToolPath { get; set; }

        /// <summary>Folder whose files are compressed and encrypted into the package.</summary>
        public string SetupFolder { get; set; }

        /// <summary>Setup file inside <see cref="SetupFolder"/> (the entry script of the package).</summary>
        public string SetupFile { get; set; }

        /// <summary>Existing, empty folder that receives the <c>.intunewin</c> file.</summary>
        public string OutputFolder { get; set; }

        public TimeSpan Timeout { get; set; }

        /// <summary>Allows a tool whose SHA-256 is not a known version. Only after an explicit confirmation of the user.</summary>
        public bool AllowUnknownTool { get; set; }
    }

    /// <summary>Packs a folder into a <c>.intunewin</c> file. The interface lets the build pipeline be tested without the real tool.</summary>
    public interface IContentPrepRunner
    {
        /// <summary>Returns the path of the verified <c>.intunewin</c> file inside <see cref="PackRequest.OutputFolder"/>.</summary>
        string Pack(PackRequest request);
    }

    /// <summary>
    /// Runs the Win32 Content Prep Tool (<c>IntuneWinAppUtil.exe -c folder -s file -o folder -q</c>) with an argument
    /// list, checks the tool's SHA-256 against the known versions first, and verifies the produced file.
    /// </summary>
    public sealed class ContentPrepTool : IContentPrepRunner
    {
        /// <summary>
        /// Known versions with the SHA-256 of <c>IntuneWinAppUtil.exe</c> as published in the tag of the
        /// Microsoft repository (provenance in <c>THIRD-PARTY.md</c>).
        /// </summary>
        public static readonly IReadOnlyList<ContentPrepToolVersion> KnownVersions = new[]
        {
            new ContentPrepToolVersion("1.8.7", "c1ba45b5cb939e84af064bb7ff4b38fb3dfe33c8dc1078fd9b157672eae671f6")
        };

        public static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>Version of a tool file when its SHA-256 is known, otherwise null.</summary>
        public static string IdentifyVersion(string path)
        {
            var hash = ComputeSha256(path);
            foreach (var known in KnownVersions)
            {
                if (string.Equals(known.Sha256, hash, StringComparison.Ordinal))
                {
                    return known.Version;
                }
            }

            return null;
        }

        public string Pack(PackRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (string.IsNullOrWhiteSpace(request.ToolPath) || !File.Exists(request.ToolPath))
            {
                throw new ContentPrepException(ContentPrepProblem.ToolMissing, request.ToolPath);
            }

            if (!Directory.Exists(request.SetupFolder))
            {
                throw new ContentPrepException(ContentPrepProblem.InputMissing, request.SetupFolder);
            }

            var setupPath = Path.Combine(request.SetupFolder, request.SetupFile ?? string.Empty);
            if (string.IsNullOrWhiteSpace(request.SetupFile) || !File.Exists(setupPath))
            {
                throw new ContentPrepException(ContentPrepProblem.InputMissing, setupPath);
            }

            if (!request.AllowUnknownTool && IdentifyVersion(request.ToolPath) == null)
            {
                throw new ContentPrepException(ContentPrepProblem.ToolNotRecognized, request.ToolPath + " (SHA-256 " + ComputeSha256(request.ToolPath) + ")");
            }

            Directory.CreateDirectory(request.OutputFolder);

            ProcessResult result;
            try
            {
                result = ProcessRunner.Run(
                    request.ToolPath,
                    new[] { "-c", request.SetupFolder, "-s", request.SetupFile, "-o", request.OutputFolder, "-q" },
                    request.OutputFolder,
                    request.Timeout);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new ContentPrepException(ContentPrepProblem.ToolNotStartable, request.ToolPath + " (" + exception.Message + ")");
            }

            if (result.TimedOut)
            {
                throw new ContentPrepException(ContentPrepProblem.ToolTimedOut, "after " + request.Timeout);
            }

            if (result.ExitCode != 0)
            {
                throw new ContentPrepException(ContentPrepProblem.ToolFailed, "exit code " + result.ExitCode + Environment.NewLine + result.StandardOutput + result.StandardError);
            }

            var produced = Path.Combine(request.OutputFolder, Path.GetFileNameWithoutExtension(request.SetupFile) + ".intunewin");
            IntunewinVerifier.Verify(produced);
            return produced;
        }
    }
}
