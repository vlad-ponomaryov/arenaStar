using System;
using System.Globalization;
using System.Threading;

namespace Arena.LoadTest
{
    /// <summary>
    /// Entry point of <c>Arena.LoadTest</c> (Server-Spec §11.2, S3.15): N matches of light clients over UDP against the real
    /// server on this PC (<see cref="LoadRunner"/>). Run it with <c>Tools/server-load.ps1</c>. Exit codes: 0 within the
    /// thresholds, 1 failed, 2 bad arguments.
    /// </summary>
    public static class LoadTestProgram
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            LoadTestOptions options = LoadTestOptions.Parse(args, out string error);
            if (options == null)
            {
                Console.WriteLine(error);
                Console.WriteLine(LoadTestOptions.Usage);
                return 2;
            }
            try
            {
                return new LoadRunner(options, Console.Out).Run() ? 0 : 1;
            }
            catch (Exception e)
            {
                Console.WriteLine("LOADTEST: FAILED, " + e.GetType().Name + ": " + e.Message);
                Console.WriteLine(e);
                return 1;
            }
        }
    }
}
