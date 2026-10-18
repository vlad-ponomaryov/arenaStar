using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Arena.Server.Host;

namespace Arena.Server
{
    /// <summary>
    /// Self-test of <c>server.json</c> handling (Server-Spec §5.5, S3.9): the defaults survive write, read, write unchanged, a
    /// ru-RU thread culture does not turn "0.5" into "0,5" either way, unknown keys and wrong types are refused with the key in
    /// the message, and the committed <c>config/server.json</c> next to the server loads and validates.
    /// </summary>
    public static class ConfigSelfTest
    {
        public static string Run(TextWriter output)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                // Like ru-RU (a comma separator); built by hand because the server runs in invariant globalization mode.
                var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                comma.NumberFormat.NumberDecimalSeparator = ",";
                Thread.CurrentThread.CurrentCulture = comma;
                string json = ServerSettingsLoader.ToJson(ServerConfig.CreateDefault());
                if (json.IndexOf("1.5", StringComparison.Ordinal) < 0) return "BotTakeoverSeconds 1.5 not written with a dot";
                string again = ServerSettingsLoader.ToJson(ServerSettingsLoader.Parse(json));
                if (again != json) return "defaults change after write, read, write";

                ServerConfig custom = ServerSettingsLoader.Parse(
                    "{ // comment\n \"Host\": { \"UdpPort\": 7000, \"LogLevel\": \"warn\" },\n \"Server\": { \"AfkSeconds\": 12.5, \"Snapshots\": { \"AoiRadiusCellsLastBeast\": 14.25 } },\n \"Transport\": { \"LanDiscovery\": false, },\n}");
                if (custom.Host.UdpPort != 7000 || custom.Host.LogLevel != LogLevel.Warn || custom.Server.AfkSeconds != 12.5f
                    || custom.Server.Snapshots.AoiRadiusCellsLastBeast != 14.25f || custom.Transport.LanDiscovery)
                    return "custom values not applied";
                if (custom.Server.BotTakeoverSeconds != 1.5f || custom.Host.MetricsPort != 7778) return "keys left out lost their defaults";

                string error = Refused("{ \"Server\": { \"AfkSecond\": 3 } }", "Server.AfkSecond");
                if (error != null) return error;
                error = Refused("{ \"Host\": { \"UdpPort\": \"7777\" } }", "Host.UdpPort");
                if (error != null) return error;
                error = Refused("{ \"Server\": { \"FillBotDifficulty\": \"Nightmare\" } }", "Server.FillBotDifficulty");
                if (error != null) return error;
                error = Refused("{ \"Host\": { \"WorkerThreads\": 2 } }", "WorkerThreads");
                if (error != null) return error;
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }

            string committed = Path.Combine(AppContext.BaseDirectory, "config", "server.json");
            if (!File.Exists(committed)) return "no " + committed + " (the build copies Server/config)";
            ServerConfig file = ServerSettingsLoader.LoadFile(committed);
            output.WriteLine("        config: " + committed + ": udp " + file.Host.UdpPort + ", metrics " + file.Host.MetricsBindAddress + ":"
                + file.Host.MetricsPort + ", max matches " + file.Server.MaxMatches + ", drain " + file.Host.DrainSeconds + " s");
            return null;
        }

        private static string Refused(string json, string key)
        {
            try
            {
                ServerSettingsLoader.Parse(json);
            }
            catch (ArgumentException e)
            {
                return e.Message.IndexOf(key, StringComparison.Ordinal) >= 0 ? null : "error for " + key + " does not name it: " + e.Message;
            }
            return "accepted " + json;
        }
    }
}
