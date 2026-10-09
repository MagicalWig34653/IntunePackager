using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.App.Tests
{
    public class ResourceTests
    {
        private static IEnumerable<string> KeysBuiltFromCodes()
        {
            foreach (var value in Enum.GetValues(typeof(ImportProblem)))
            {
                yield return "Import_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(AnalysisProblem)))
            {
                yield return "Analysis_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(AnalysisNote)))
            {
                yield return "Note_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(ValidationCode)))
            {
                yield return "Valid_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(BuildPhase)))
            {
                yield return "Phase_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(BuildProblem)))
            {
                yield return "Build_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(ContentPrepProblem)))
            {
                yield return "ContentPrep_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(WorkflowProblem)))
            {
                yield return "Workflow_" + value;
            }

            foreach (var value in Enum.GetValues(typeof(BaseFolderStatus)))
            {
                yield return "BaseFolder_" + value;
            }
        }

        [Theory]
        [InlineData("en")]
        [InlineData("de")]
        public void EveryTextTheCodesCanProduceExistsInBothLanguages(string culture)
        {
            Loc.Use(new CultureInfo(culture));
            var missing = KeysBuiltFromCodes().Where(k => Loc.Get(k) == "!" + k + "!").ToList();

            Assert.Empty(missing);
        }

        [Fact]
        public void TheGermanTextsDifferFromTheEnglishOnes()
        {
            Loc.Use(new CultureInfo("en"));
            var english = KeysBuiltFromCodes().ToDictionary(k => k, Loc.Get);
            Loc.Use(new CultureInfo("de"));
            var same = english.Where(pair => Loc.Get(pair.Key) == pair.Value).Select(pair => pair.Key).ToList();

            Assert.Empty(same);
        }

        [Fact]
        public void TheLanguageOfTheGeneratedTextsFollowsTheProgramLanguage()
        {
            Loc.Use(new CultureInfo("de-DE"));
            Assert.Equal("de", Loc.Language);
            Loc.Use(new CultureInfo("fr-FR"));
            Assert.Equal("en", Loc.Language);
            Loc.Use(new CultureInfo("en-US"));
            Assert.Equal("en", Loc.Language);
        }

        [Fact]
        public void AMissingKeyIsVisible()
        {
            Loc.Use(new CultureInfo("en"));

            Assert.Equal("!No_Such_Key!", Loc.Get("No_Such_Key"));
        }
    }
}
