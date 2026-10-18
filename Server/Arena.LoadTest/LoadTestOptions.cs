using System;
using System.Globalization;
using Arena.Core.Modes;

namespace Arena.LoadTest
{
    /// <summary>
    /// Command line of the load test (Server-Spec §11.2, S3.15). Defaults: 100 Acorns matches (600 light clients), 60 s of
    /// measurement after all matches run, server on 127.0.0.1:17777 (metrics 17778) — ports apart from a server on 7777.
    /// Pass thresholds of the server (one game thread, §5.3): no skipped tick, tick lateness p99 ≤ 5 ms, the server process
    /// below 0.8 of one core.
    /// </summary>
    public sealed class LoadTestOptions
    {
        public const string Usage =
            "Usage: Arena.LoadTest [--matches N] [--seconds S] [--mode acorns|lastbeast] [--port P] [--metrics-port P] [--client-threads N] [--server-dll path]";

        public int Matches = 100;
        public float MeasureSeconds = 60f;
        public float WarmupSeconds = 5f;
        public float StartTimeoutSeconds = 90f;
        public int RampClientsPerSecond = 300;
        public ModeKind Mode = ModeKind.Acorns;
        public int Port = 17777;
        public int MetricsPort = 17778;
        public int ClientThreads;
        public string ServerDll;

        public double MaxLatenessP99Ms = 5.0;
        public double MaxServerCores = 0.8;

        public int PlayersPerMatch => Mode == ModeKind.LastBeast ? 10 : 6;

        public static LoadTestOptions Parse(string[] args, out string error)
        {
            var o = new LoadTestOptions();
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string v = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--matches":
                        if (!Int(v, 1, 1000, out o.Matches)) return Fail(a, out error);
                        i++;
                        break;
                    case "--seconds":
                        if (!Float(v, 5f, 3600f, out o.MeasureSeconds)) return Fail(a, out error);
                        i++;
                        break;
                    case "--port":
                        if (!Int(v, 1, 65535, out o.Port)) return Fail(a, out error);
                        i++;
                        break;
                    case "--metrics-port":
                        if (!Int(v, 1, 65535, out o.MetricsPort)) return Fail(a, out error);
                        i++;
                        break;
                    case "--client-threads":
                        if (!Int(v, 1, 64, out o.ClientThreads)) return Fail(a, out error);
                        i++;
                        break;
                    case "--mode":
                        if (v == "acorns") o.Mode = ModeKind.Acorns;
                        else if (v == "lastbeast") o.Mode = ModeKind.LastBeast;
                        else return Fail(a, out error);
                        i++;
                        break;
                    case "--server-dll":
                        if (string.IsNullOrEmpty(v)) return Fail(a, out error);
                        o.ServerDll = v;
                        i++;
                        break;
                    default:
                        error = "Unknown argument: " + a;
                        return null;
                }
            }
            if (o.ClientThreads == 0) o.ClientThreads = Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2));
            return o;
        }

        private static LoadTestOptions Fail(string arg, out string error)
        {
            error = "Bad value for " + arg;
            return null;
        }

        private static bool Int(string s, int min, int max, out int value) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

        private static bool Float(string s, float min, float max, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
    }
}
