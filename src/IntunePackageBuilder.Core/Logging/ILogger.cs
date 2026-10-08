using System;

namespace IntunePackageBuilder.Core.Logging
{
    /// <summary>Minimal logging abstraction so modules do not depend on a concrete sink.</summary>
    public interface ILogger
    {
        void Log(LogLevel level, string message, Exception exception = null);
    }
}
