using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace IntunePackageBuilder.Build.Processes
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }

        /// <summary>True when the process did not end within the time limit and was killed.</summary>
        public bool TimedOut { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    /// <summary>
    /// Starts an external program from an argument list without a shell, with redirected and drained output, an
    /// empty standard input (so a program that asks for input ends instead of waiting) and a time limit.
    /// </summary>
    public static class ProcessRunner
    {
        private const int MaxCapturedCharacters = 1024 * 1024;

        public static ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("A program is required.", "fileName");
            }

            var start = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = ArgumentQuoter.Join(arguments ?? new string[0]),
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += (sender, e) => Append(output, e.Data);
                process.ErrorDataReceived += (sender, e) => Append(error, e.Data);
                process.Start();
                process.StandardInput.Close();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timedOut = false;
                if (!process.WaitForExit((int)Math.Min(int.MaxValue, Math.Max(1, timeout.TotalMilliseconds))))
                {
                    timedOut = true;
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process ended between the wait and the kill.
                    }
                }

                // The parameterless overload waits until the asynchronous output readers have finished.
                process.WaitForExit();
                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    TimedOut = timedOut,
                    StandardOutput = Snapshot(output),
                    StandardError = Snapshot(error)
                };
            }
        }

        private static void Append(StringBuilder target, string line)
        {
            if (line == null)
            {
                return;
            }

            lock (target)
            {
                if (target.Length < MaxCapturedCharacters)
                {
                    target.AppendLine(line);
                }
            }
        }

        private static string Snapshot(StringBuilder source)
        {
            lock (source)
            {
                return source.ToString();
            }
        }
    }
}
