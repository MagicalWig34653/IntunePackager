using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using IntunePackageBuilder.Core.Logging;

namespace IntunePackageBuilder.App.Services
{
    /// <summary>Explorer, the default browser and the clipboard behind <see cref="IShell"/>. A failure is logged and never crashes the program.</summary>
    public sealed class ShellImpl : IShell
    {
        private readonly ILogger _logger;

        public ShellImpl(ILogger logger)
        {
            _logger = logger;
        }

        public void OpenFolder(string path)
        {
            Start(path);
        }

        public void OpenFile(string path)
        {
            Start(path);
        }

        public void OpenUrl(string url)
        {
            // Only web links are opened; nothing else is handed to the shell from a text.
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                Start(uri.AbsoluteUri);
            }
        }

        public void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
            }
            catch (COMException exception)
            {
                // The clipboard can be locked by another program for a moment.
                _logger.Log(LogLevel.Warning, "The clipboard could not be written", exception);
            }
        }

        private void Start(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                _logger.Log(LogLevel.Warning, "Could not open " + target, exception);
            }
        }
    }
}
