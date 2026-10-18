using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Server.Host;

namespace Arena.Server
{
    /// <summary>
    /// Entry point of the dedicated server (Server-Spec §5). The content pack next to the dll is loaded and verified first.
    /// <list type="bullet">
    /// <item><c>Arena.Server [--config path]</c> — run the server (default config: <c>config/server.json</c> next to the dll;
    /// missing — the defaults of §13); Ctrl+C / SIGTERM drain it (§5.3), a second one stops at once.</item>
    /// <item><c>--print-config [--config path]</c> — the effective config as JSON.</item>
    /// <item><c>--version</c>; <c>--selftest [--match-seconds N]</c> (§11.2).</item>
    /// </list>
    /// Exit codes: 0 ok, 1 self-test failed, 2 bad arguments, 3 content pack missing or broken, 4 bad config or the port is
    /// busy, 5 the game loop crashed.
    /// </summary>
    public static class Program
    {
        public const int ExitOk = 0;
        public const int ExitSelfTestFailed = 1;
        public const int ExitUsage = 2;
        public const int ExitBadContent = 3;
        public const int ExitBadConfig = 4;
        public const int ExitCrashed = 5;

        public const string DefaultConfigRelativePath = "config/server.json";

        private const string Usage =
            "Usage: Arena.Server [--config <server.json>] | --print-config [--config <server.json>] | --version | --selftest [--match-seconds <N>]";

        public static int Main(string[] args)
        {
            // Must be first: with ru-RU numbers print as "0,5" and break Prometheus metrics and log parsing (§5.2).
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            if (!ParseArgs(args, out string mode, out string configPath, out float matchSeconds, out string argError))
            {
                Console.WriteLine(argError);
                Console.WriteLine(Usage);
                return ExitUsage;
            }

            ServerContent content;
            try
            {
                content = ServerContent.Load(ServerContent.DefaultPath);
            }
            catch (Exception e)
            {
                Console.WriteLine(VersionLine(null));
                Console.WriteLine("Content pack error: " + e.GetType().Name + ": " + e.Message);
                return ExitBadContent;
            }

            switch (mode)
            {
                case "version":
                    Console.WriteLine(VersionLine(content));
                    return ExitOk;
                case "selftest":
                    Console.WriteLine(VersionLine(content));
                    return SelfTest.Run(Console.Out, content, matchSeconds) ? ExitOk : ExitSelfTestFailed;
                case "print-config":
                {
                    ServerConfig config = LoadConfig(configPath, null, out string error);
                    if (config == null)
                    {
                        Console.WriteLine(error);
                        return ExitBadConfig;
                    }
                    Console.WriteLine(ServerSettingsLoader.ToJson(config));
                    return ExitOk;
                }
                default:
                    return RunServer(content, configPath);
            }
        }

        private static bool ParseArgs(string[] args, out string mode, out string configPath, out float matchSeconds, out string error)
        {
            mode = "run";
            configPath = null;
            matchSeconds = HostSelfTest.DefaultMatchSeconds;
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--version":
                        mode = "version";
                        break;
                    case "--selftest":
                        mode = "selftest";
                        break;
                    case "--print-config":
                        mode = "print-config";
                        break;
                    case "--config":
                        if (i + 1 >= args.Length)
                        {
                            error = "--config needs a path";
                            return false;
                        }
                        configPath = args[++i];
                        break;
                    case "--match-seconds":
                        if (i + 1 >= args.Length || !float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out matchSeconds)
                            || matchSeconds < 1f)
                        {
                            error = "--match-seconds needs a number >= 1";
                            return false;
                        }
                        i++;
                        break;
                    default:
                        error = "Unknown argument: " + a;
                        return false;
                }
            }
            return true;
        }

        private static ServerConfig LoadConfig(string configPath, ConsoleLog log, out string error)
        {
            error = null;
            string path = configPath ?? Path.Combine(AppContext.BaseDirectory, DefaultConfigRelativePath);
            try
            {
                if (configPath == null && !File.Exists(path))
                {
                    log?.Warn("no " + path + ": running with the defaults (Server-Spec §13)");
                    var defaults = ServerConfig.CreateDefault();
                    defaults.Validate();
                    return defaults;
                }
                ServerConfig config = ServerSettingsLoader.LoadFile(path);
                log?.Info("config " + Path.GetFullPath(path));
                return config;
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
            {
                error = "Config error: " + e.Message;
                return null;
            }
        }

        private static int RunServer(ServerContent content, string configPath)
        {
            var log = new ConsoleLog(Console.Out, LogLevel.Info);
            log.Info(VersionLine(content));
            ServerConfig config = LoadConfig(configPath, log, out string error);
            if (config == null)
            {
                log.Error(error);
                return ExitBadConfig;
            }
            log.Level = config.Host.LogLevel;
            using (var shutdown = new ShutdownHandler())
            using (var host = new ServerHost(config, content, log, shutdown))
            {
                try
                {
                    host.Start();
                }
                catch (Exception e) when (e is System.Net.Sockets.SocketException || e is FormatException || e is ArgumentException)
                {
                    log.Error("cannot start: " + e.Message);
                    return ExitBadConfig;
                }
                shutdown.InstallSignalHandlers();
                try
                {
                    host.Run();
                }
                catch (Exception e)
                {
                    log.Error("game loop crashed: " + e);
                    return ExitCrashed;
                }
                log.Info("stopped (" + host.StopReason + ")");
            }
            return ExitOk;
        }

        public static string VersionLine(ServerContent content)
        {
            Version asm = typeof(Program).Assembly.GetName().Version;
            string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            return "Arena.Server " + (asm == null ? "0.0.0.0" : asm.ToString())
                + " protocol 0x" + NetProtocol.Id.ToString("X8", CultureInfo.InvariantCulture)
                + " v" + NetProtocol.Version.ToString(CultureInfo.InvariantCulture)
                + " content " + (content == null ? "none" : ContentPackHeader.FormatHash(content.Hash))
                + " build " + BuildFlavor()
                + " runtime " + runtime;
        }

        private static string BuildFlavor()
        {
#if NET8_0_OR_GREATER
            return "sdk";
#else
            return "bootstrap";
#endif
        }
    }
}
