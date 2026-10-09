using System;
using System.Collections.Generic;
using System.Text;

namespace IntunePackageBuilder.Build.Processes
{
    /// <summary>
    /// Builds a Windows command line from an argument list following the rules of <c>CommandLineToArgvW</c> and
    /// the C runtime, so every argument arrives at the child exactly as given. External processes are always started
    /// from an argument list, never from a shell string (SPEC section 10).
    /// </summary>
    public static class ArgumentQuoter
    {
        public static string Join(IEnumerable<string> arguments)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException("arguments");
            }

            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                Append(builder, argument);
            }

            return builder.ToString();
        }

        public static string Quote(string argument)
        {
            var builder = new StringBuilder();
            Append(builder, argument);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string argument)
        {
            if (argument == null)
            {
                throw new ArgumentNullException("argument");
            }

            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    // Backslashes before a quote are doubled, and the quote itself is escaped.
                    builder.Append('\\', (backslashes * 2) + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(c);
                }

                backslashes = 0;
            }

            // Backslashes at the end are doubled so they do not escape the closing quote.
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
        }
    }
}
