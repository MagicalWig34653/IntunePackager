using System.Collections.Generic;
using System.IO;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Generation.Guide;
using IntunePackageBuilder.Generation.Intune;
using IntunePackageBuilder.Generation.Scripts;
using IntunePackageBuilder.Generation.Wrapper;

namespace IntunePackageBuilder.Generation
{
    /// <summary>
    /// Generates every text artifact of a build from one snapshot: the detection script, the guide, the settings
    /// (JSON and CSV) and the wrapper configuration. Writing the files into the build folder and the package
    /// content is the job of the build pipeline (M4); this class only knows the file names.
    /// </summary>
    public static class BuildArtifacts
    {
        /// <summary>
        /// Writes the files of the <c>intune</c> folder (<c>Detect-App.ps1</c>, <c>Einrichtung.html</c>,
        /// <c>Einstellungen.json</c>, <c>Einstellungen.csv</c>) into <paramref name="intuneDirectory"/> and
        /// <c>Deployment.config.json</c> into <paramref name="packageRootDirectory"/>. Returns the written paths.
        /// </summary>
        public static IReadOnlyList<string> Write(BuildSnapshot snapshot, IntuneSettingsOptions options, string intuneDirectory, string packageRootDirectory)
        {
            var settings = IntuneSettingsBuilder.From(snapshot, options ?? new IntuneSettingsOptions());
            var written = new List<string>();

            var script = Path.Combine(intuneDirectory, DeploymentInterface.DetectionScript);
            DetectionScriptGenerator.WriteTo(script, DetectionScriptGenerator.Generate(settings));
            written.Add(script);

            var guide = Path.Combine(intuneDirectory, DeploymentInterface.GuideFile);
            GuideGenerator.WriteTo(guide, GuideGenerator.Generate(settings));
            written.Add(guide);

            SettingsWriter.WriteFiles(settings, intuneDirectory);
            written.Add(Path.Combine(intuneDirectory, DeploymentInterface.SettingsJsonFile));
            written.Add(Path.Combine(intuneDirectory, DeploymentInterface.SettingsCsvFile));

            var wrapper = Path.Combine(packageRootDirectory, DeploymentInterface.WrapperConfigFile);
            WrapperConfigBuilder.WriteTo(wrapper, WrapperConfigBuilder.From(snapshot, settings));
            written.Add(wrapper);

            return written;
        }
    }
}
