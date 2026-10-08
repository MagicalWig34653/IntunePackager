using System;

namespace IntunePackageBuilder.Core.Projects
{
    public sealed class InvalidProjectIdException : ArgumentException
    {
        public InvalidProjectIdException(string id, ProjectIdProblem problem)
            : base("Project ID '" + id + "' is not valid: " + problem, "id")
        {
            Problem = problem;
        }

        public ProjectIdProblem Problem { get; private set; }
    }

    public sealed class ProjectExistsException : Exception
    {
        public ProjectExistsException(string projectId)
            : base("A project with ID '" + projectId + "' already exists.")
        {
            ProjectId = projectId;
        }

        public string ProjectId { get; private set; }
    }

    public sealed class ProjectNotFoundException : Exception
    {
        public ProjectNotFoundException(string projectId)
            : base("Project '" + projectId + "' was not found.")
        {
            ProjectId = projectId;
        }

        public string ProjectId { get; private set; }
    }
}
