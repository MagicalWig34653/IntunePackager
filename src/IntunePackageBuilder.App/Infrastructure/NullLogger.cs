using System;
using IntunePackageBuilder.Core.Logging;

namespace IntunePackageBuilder.App.Infrastructure
{
    /// <summary>Logger that discards everything (tests, and the moment before the real log exists).</summary>
    public sealed class NullLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception exception = null)
        {
        }
    }
}
