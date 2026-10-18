using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Arena.LoadTest
{
    /// <summary>
    /// The server under load as its own process (the real <c>Arena.Server</c> with a generated config), so that its CPU time and
    /// memory are measured apart from the clients. Its console output goes to a log file next to the config.
    /// </summary>
    public sealed class ServerProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _log;
        private readonly object _lock = new object();
        private int _errorLines;

        public ServerProcess(string dotnet, string serverDll, string configPath, string logPath)
        {
            _log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
            var info = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(serverDll)
            };
            info.ArgumentList.Add(serverDll);
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add(configPath);
            _process = new Process { StartInfo = info };
            _process.OutputDataReceived += (s, e) => Line(e.Data);
            _process.ErrorDataReceived += (s, e) => Line(e.Data);
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

        /// <summary>Lines of the server log with " ERROR " (none expected under load).</summary>
        public int ErrorLines
        {
            get { lock (_lock) return _errorLines; }
        }

        public TimeSpan CpuTime
        {
            get
            {
                _process.Refresh();
                return _process.TotalProcessorTime;
            }
        }

        public long WorkingSetBytes
        {
            get
            {
                _process.Refresh();
                return _process.WorkingSet64;
            }
        }

        public long PeakWorkingSetBytes
        {
            get
            {
                _process.Refresh();
                return _process.PeakWorkingSet64;
            }
        }

        public long PrivateBytes
        {
            get
            {
                _process.Refresh();
                return _process.PrivateMemorySize64;
            }
        }

        private void Line(string line)
        {
            if (line == null) return;
            lock (_lock)
            {
                if (line.IndexOf(" ERROR ", StringComparison.Ordinal) >= 0 || line.IndexOf("[ERROR]", StringComparison.Ordinal) >= 0) _errorLines++;
                _log.WriteLine(line);
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            lock (_lock) _log.Dispose();
            _process.Dispose();
        }
    }
}
