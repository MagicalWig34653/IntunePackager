using System;

namespace IntunePackageBuilder.Build.Packaging
{
    public enum ContentPrepProblem
    {
        /// <summary>No path to the tool is configured or the file does not exist.</summary>
        ToolMissing,

        /// <summary>The tool's SHA-256 is not one of the known versions and the use of unknown versions was not confirmed.</summary>
        ToolNotRecognized,

        /// <summary>The tool could not be started.</summary>
        ToolNotStartable,

        /// <summary>The tool ended with a non-zero exit code.</summary>
        ToolFailed,

        /// <summary>The tool did not end within the time limit and was stopped.</summary>
        ToolTimedOut,

        /// <summary>The tool reported success but the expected <c>.intunewin</c> file is not there.</summary>
        OutputMissing,

        /// <summary>The produced file is empty or does not have the structure of a <c>.intunewin</c> file.</summary>
        OutputInvalid,

        /// <summary>The setup folder or setup file for the tool does not exist.</summary>
        InputMissing
    }

    /// <summary>Packaging with the Content Prep Tool failed. Carries a code, never display text; the log keeps the details.</summary>
    public sealed class ContentPrepException : Exception
    {
        public ContentPrepException(ContentPrepProblem problem, string detail)
            : base(problem + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail))
        {
            Problem = problem;
            Detail = detail ?? string.Empty;
        }

        public ContentPrepProblem Problem { get; private set; }

        /// <summary>Technical detail for the log (exit code, tool output, path).</summary>
        public string Detail { get; private set; }
    }
}
