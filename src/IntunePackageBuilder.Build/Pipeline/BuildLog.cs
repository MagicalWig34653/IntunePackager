using System;
using System.Globalization;
using System.Text;

namespace IntunePackageBuilder.Build.Pipeline
{
    /// <summary>
    /// The log of one build (<c>build.log</c>): phase, time, build ID, tool versions and errors (SPEC section 10).
    /// Plain text, one line per event, UTC.
    /// </summary>
    public sealed class BuildLog
    {
        private readonly StringBuilder _text = new StringBuilder();
        private readonly Func<DateTime> _clock;

        public BuildLog(Func<DateTime> clock)
        {
            _clock = clock;
        }

        public void Info(BuildPhase? phase, string message)
        {
            Write("INFO ", phase, message);
        }

        public void Error(BuildPhase? phase, string message)
        {
            Write("ERROR", phase, message);
        }

        public override string ToString()
        {
            lock (_text)
            {
                return _text.ToString();
            }
        }

        private void Write(string level, BuildPhase? phase, string message)
        {
            var line = _clock().ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                + " " + level + " " + (phase.HasValue ? phase.Value.ToString() : "-") + " " + (message ?? string.Empty).Replace("\r\n", "\n").Replace('\n', ' ');
            lock (_text)
            {
                _text.Append(line).Append("\r\n");
            }
        }
    }
}
