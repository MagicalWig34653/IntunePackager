using IntunePackageBuilder.Core.Projects;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class ProjectIdTests
    {
        [Theory]
        [InlineData("contoso-reader")]
        [InlineData("a")]
        [InlineData("7zip")]
        [InlineData("app-2")]
        public void Check_AcceptsValidIds(string id)
        {
            Assert.Equal(ProjectIdProblem.None, ProjectId.Check(id));
            Assert.True(ProjectId.IsValid(id));
        }

        [Theory]
        [InlineData(null, ProjectIdProblem.Empty)]
        [InlineData("", ProjectIdProblem.Empty)]
        [InlineData("Upper", ProjectIdProblem.InvalidCharacters)]
        [InlineData("has space", ProjectIdProblem.InvalidCharacters)]
        [InlineData("dot.name", ProjectIdProblem.InvalidCharacters)]
        [InlineData("under_score", ProjectIdProblem.InvalidCharacters)]
        [InlineData("..", ProjectIdProblem.InvalidCharacters)]
        [InlineData("a/b", ProjectIdProblem.InvalidCharacters)]
        [InlineData("a\\b", ProjectIdProblem.InvalidCharacters)]
        [InlineData("-lead", ProjectIdProblem.InvalidEdge)]
        [InlineData("trail-", ProjectIdProblem.InvalidEdge)]
        [InlineData("con", ProjectIdProblem.ReservedName)]
        [InlineData("nul", ProjectIdProblem.ReservedName)]
        [InlineData("com1", ProjectIdProblem.ReservedName)]
        [InlineData("lpt9", ProjectIdProblem.ReservedName)]
        public void Check_RejectsInvalidIds(string id, ProjectIdProblem expected)
        {
            Assert.Equal(expected, ProjectId.Check(id));
            Assert.False(ProjectId.IsValid(id));
        }

        [Fact]
        public void Check_RejectsTooLongIds()
        {
            Assert.Equal(ProjectIdProblem.TooLong, ProjectId.Check(new string('a', ProjectId.MaxLength + 1)));
            Assert.Equal(ProjectIdProblem.None, ProjectId.Check(new string('a', ProjectId.MaxLength)));
        }

        [Theory]
        [InlineData("Contoso Reader 4.2", "contoso-reader-4-2")]
        [InlineData("  Spaces   everywhere  ", "spaces-everywhere")]
        [InlineData("M\u00fcller Tool", "mueller-tool")]
        [InlineData("Gro\u00dfe App", "grosse-app")]
        [InlineData("Café", "cafe")]
        [InlineData("***", "project")]
        [InlineData("", "project")]
        [InlineData(null, "project")]
        [InlineData("CON", "con-project")]
        public void Suggest_BuildsReadableIds(string displayName, string expected)
        {
            Assert.Equal(expected, ProjectId.Suggest(displayName));
        }

        [Fact]
        public void Suggest_AlwaysReturnsValidIds()
        {
            var inputs = new[]
            {
                new string('x', 200),
                "a" + new string('-', 80) + "b",
                "\u0001\u0002",
                "Name With: Special / Chars \\ <> | ?",
                "aux",
                "com9"
            };

            foreach (var input in inputs)
            {
                var suggestion = ProjectId.Suggest(input);
                Assert.True(ProjectId.IsValid(suggestion), "Suggestion '" + suggestion + "' is not valid");
            }
        }
    }
}
