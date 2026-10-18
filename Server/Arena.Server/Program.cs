using System;
using System.Globalization;
using System.Threading;
using Arena.Net.Protocol;

namespace Arena.Server
{
    /// <summary>
    /// Entry point of the dedicated server (Server-Spec §5). Stage 3.1: only <c>--version</c> and <c>--selftest</c>;
    /// the game loop, sockets and metrics arrive in S3.9. Exit codes: 0 ok, 1 self-test failed, 2 bad arguments.
    /// </summary>
    public static class Program
    {
        public const int ExitOk = 0;
        public const int ExitSelfTestFailed = 1;
        public const int ExitUsage = 2;

        public static int Main(string[] args)
        {
            // Must be first: with ru-RU numbers print as "0,5" and break Prometheus metrics and log parsing (§5.2).
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            if (args.Length == 1 && args[0] == "--version")
            {
                Console.WriteLine(VersionLine());
                return ExitOk;
            }
            if (args.Length == 1 && args[0] == "--selftest")
            {
                Console.WriteLine(VersionLine());
                return SelfTest.Run(Console.Out) ? ExitOk : ExitSelfTestFailed;
            }
            Console.WriteLine(VersionLine());
            Console.WriteLine("Usage: Arena.Server --version | --selftest   (the game server loop arrives in step S3.9)");
            return args.Length == 0 ? ExitOk : ExitUsage;
        }

        public static string VersionLine()
        {
            Version asm = typeof(Program).Assembly.GetName().Version;
            string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            return "Arena.Server " + (asm == null ? "0.0.0.0" : asm.ToString())
                + " protocol 0x" + NetProtocol.Id.ToString("X8", CultureInfo.InvariantCulture)
                + " v" + NetProtocol.Version.ToString(CultureInfo.InvariantCulture)
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
