using System;
using System.Text;

namespace IntunePackageBuilder.Generation.Scripts
{
    /// <summary>A value cannot be placed into a generated script without changing what the script does.</summary>
    public sealed class UnsafeScriptValueException : ArgumentException
    {
        public UnsafeScriptValueException(string paramName, string message)
            : base(message, paramName)
        {
        }
    }

    /// <summary>
    /// Renders text as a PowerShell single-quoted string literal. Single-quoted strings do no expansion; the only
    /// characters that end them are the quote characters. PowerShell treats the typographic single quotes as quotes
    /// as well, so all of them are doubled. Characters that could start a new line or hide content are rejected
    /// instead of being escaped: a value that contains them is not a legitimate path, version or product code.
    /// </summary>
    public static class PowerShellLiteral
    {
        public static string SingleQuoted(string value, string name)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('\'');
            foreach (var c in value)
            {
                if (IsForbidden(c))
                {
                    throw new UnsafeScriptValueException(name, "The value of '" + name + "' contains a control or line separator character.");
                }

                builder.Append(c);
                if (IsQuote(c))
                {
                    builder.Append(c);
                }
            }

            builder.Append('\'');
            return builder.ToString();
        }

        private static bool IsQuote(char c)
        {
            return c == '\'' || c == '‘' || c == '’' || c == '‚' || c == '‛';
        }

        private static bool IsForbidden(char c)
        {
            return c < ' ' || c == '\u007F' || (c >= '\u0080' && c <= '\u009F') || c == ' ' || c == ' '
                || (c >= '​' && c <= '‏') || (c >= '‪' && c <= '‮') || c == '﻿';
        }
    }
}
