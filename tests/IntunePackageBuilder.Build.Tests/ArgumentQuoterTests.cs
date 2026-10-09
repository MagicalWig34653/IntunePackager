using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IntunePackageBuilder.Build.Processes;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class ArgumentQuoterTests
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argumentCount);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("-c", "-c")]
        [InlineData("C:\\Program Files\\App", "\"C:\\Program Files\\App\"")]
        [InlineData("", "\"\"")]
        [InlineData("a\"b", "\"a\\\"b\"")]
        [InlineData("C:\\dir\\", "C:\\dir\\")]
        [InlineData("C:\\my dir\\", "\"C:\\my dir\\\\\"")]
        [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
        [InlineData("tab\there", "\"tab\there\"")]
        public void ArgumentsAreQuotedByTheWindowsRules(string argument, string expected)
        {
            Assert.Equal(expected, ArgumentQuoter.Quote(argument));
        }

        [Fact]
        public void ArgumentsAreJoinedWithSpaces()
        {
            Assert.Equal("-c \"a b\" -q", ArgumentQuoter.Join(new[] { "-c", "a b", "-q" }));
        }

        [Fact]
        public void ANullArgumentIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => ArgumentQuoter.Join(new string[] { null }));
        }

        [Theory]
        [InlineData("plain")]
        [InlineData("")]
        [InlineData("C:\\Program Files\\App")]
        [InlineData("C:\\my dir\\")]
        [InlineData("a\"b")]
        [InlineData("a\\\"b")]
        [InlineData("a\\\\\"b c")]
        [InlineData("ends with backslashes\\\\")]
        [InlineData("\"quoted\"")]
        [InlineData("x & y | z > w ^ v")]
        [InlineData("%PATH% !x!")]
        [InlineData("Umlaut \u00E4\u00F6\u00FC and caf\u00E9")]
        public void TheWindowsParserReadsEveryArgumentBackExactly(string argument)
        {
            var arguments = new List<string> { "program.exe", "first", argument, "last" };
            var commandLine = ArgumentQuoter.Join(arguments);

            Assert.Equal(arguments, ParseLikeWindows(commandLine));
        }

        /// <summary>Parses with the same function the C runtime of the child process uses.</summary>
        private static List<string> ParseLikeWindows(string commandLine)
        {
            int count;
            var pointer = CommandLineToArgvW(commandLine, out count);
            Assert.NotEqual(IntPtr.Zero, pointer);
            try
            {
                var result = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)));
                }

                return result;
            }
            finally
            {
                LocalFree(pointer);
            }
        }
    }
}
