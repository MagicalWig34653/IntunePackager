using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>Turns the codes of the lower layers into localized texts. Every key it builds exists in both resource files (a test checks that).</summary>
    public static class Texts
    {
        public static string ForImport(ImportRejectedException exception)
        {
            return Loc.Format("Import_" + exception.Problem, exception.Path ?? string.Empty);
        }

        public static string ForAnalysis(AnalysisFailedException exception)
        {
            return Loc.Format("Analysis_" + exception.Problem, exception.Path ?? string.Empty);
        }

        public static string ForNote(AnalysisNote note)
        {
            return Loc.Get("Note_" + note);
        }

        public static string ForValidation(ValidationCode code)
        {
            return Loc.Get("Valid_" + code);
        }

        public static string ForPhase(BuildPhase phase)
        {
            return Loc.Get("Phase_" + phase);
        }

        public static string ForBuild(BuildFailedException exception)
        {
            var text = Loc.Get("Build_" + exception.Problem);
            var cause = exception.InnerException as ContentPrepException;
            if (cause != null)
            {
                text += " " + Loc.Get("ContentPrep_" + cause.Problem);
            }

            if (exception.Problem == BuildProblem.SourceChanged || exception.Problem == BuildProblem.ConfigurationInvalid)
            {
                text += " " + Loc.Format("Error_Detail", exception.Detail);
            }

            return text;
        }

        public static string ForWorkflow(WorkflowException exception)
        {
            var text = Loc.Get("Workflow_" + exception.Problem);
            if (exception.ImportProblem.HasValue)
            {
                text = Loc.Format("Import_" + exception.ImportProblem.Value, exception.Detail);
            }

            return text;
        }
    }
}
