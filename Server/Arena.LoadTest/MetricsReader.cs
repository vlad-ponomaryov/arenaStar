using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Arena.LoadTest
{
    /// <summary>Reads the server's <c>/metrics</c> (Prometheus text, Server-Spec §5.6) into name → value; quantiles as <c>name{quantile="0.99"}</c>.</summary>
    public static class MetricsReader
    {
        private const int TimeoutMs = 3000;

        public static Dictionary<string, double> Fetch(IPEndPoint endPoint)
        {
            string body = HttpGet(endPoint, "/metrics", out int status);
            if (status != 200) return null;
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int space = line.LastIndexOf(' ');
                if (space <= 0) continue;
                if (double.TryParse(line.Substring(space + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    values[line.Substring(0, space)] = v;
            }
            return values;
        }

        public static bool Healthy(IPEndPoint endPoint)
        {
            try
            {
                return HttpGet(endPoint, "/healthz", out int status).Trim() == "ok" && status == 200;
            }
            catch (Exception e) when (e is SocketException || e is IOException)
            {
                return false;
            }
        }

        public static double Get(Dictionary<string, double> values, string name) =>
            values != null && values.TryGetValue(name, out double v) ? v : 0.0;

        private static string HttpGet(IPEndPoint endPoint, string path, out int status)
        {
            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = TimeoutMs;
                tcp.SendTimeout = TimeoutMs;
                tcp.Connect(endPoint);
                NetworkStream stream = tcp.GetStream();
                byte[] request = Encoding.ASCII.GetBytes("GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
                stream.Write(request, 0, request.Length);
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                string response = Encoding.UTF8.GetString(buffer.ToArray());
                int headerEnd = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                int lineEnd = response.IndexOf('\r');
                string[] statusLine = response.Substring(0, Math.Max(0, lineEnd)).Split(' ');
                status = statusLine.Length > 1 && int.TryParse(statusLine[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : -1;
                return headerEnd < 0 ? "" : response.Substring(headerEnd + 4);
            }
        }
    }
}
