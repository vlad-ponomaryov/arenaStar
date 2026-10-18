using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Arena.Server.Host
{
    /// <summary>
    /// Mini-HTTP on <see cref="TcpListener"/> (Server-Spec §5.6, no ASP.NET — works in the bootstrap build): <c>GET /metrics</c>
    /// (Prometheus text) and <c>GET /healthz</c> (<c>ok</c>, or 503 <c>draining</c> while stopping). One background thread
    /// answers requests one by one and only reads the snapshot <see cref="Publish"/> copied in under a lock — never the live
    /// game objects. Bound to 127.0.0.1 by default.
    /// </summary>
    public sealed class MetricsEndpoint : IDisposable
    {
        private const int MaxRequestBytes = 4096;
        private const int ReadTimeoutMs = 2000;

        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly ConsoleLog _log;
        private readonly object _lock = new object();
        private readonly MetricsSnapshot _shared = new MetricsSnapshot();
        private readonly MetricsSnapshot _local = new MetricsSnapshot();
        private volatile bool _stopping;

        public MetricsEndpoint(IPEndPoint local, ConsoleLog log)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _listener = new TcpListener(local);
            _listener.Start();
            LocalEndPoint = (IPEndPoint)_listener.LocalEndpoint;
            _thread = new Thread(Serve) { IsBackground = true, Name = "arena-metrics" };
            _thread.Start();
        }

        public IPEndPoint LocalEndPoint { get; }

        public long Requests { get; private set; }

        /// <summary>Game thread: copies <paramref name="snapshot"/> in (no allocations).</summary>
        public void Publish(MetricsSnapshot snapshot)
        {
            lock (_lock) _shared.CopyFrom(snapshot);
        }

        private void Serve()
        {
            var buffer = new byte[MaxRequestBytes];
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (_stopping) return;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                try
                {
                    using (client)
                    {
                        client.ReceiveTimeout = ReadTimeoutMs;
                        client.SendTimeout = ReadTimeoutMs;
                        Answer(client.GetStream(), buffer);
                    }
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                {
                    _log.Debug("metrics: request failed: " + e.GetType().Name);
                }
            }
        }

        private void Answer(NetworkStream stream, byte[] buffer)
        {
            int length = 0;
            while (length < buffer.Length)
            {
                int n = stream.Read(buffer, length, buffer.Length - length);
                if (n <= 0) break;
                length += n;
                if (HasHeaderEnd(buffer, length)) break;
            }
            string request = Encoding.ASCII.GetString(buffer, 0, length);
            int lineEnd = request.IndexOf('\r');
            string line = lineEnd >= 0 ? request.Substring(0, lineEnd) : request;
            string[] parts = line.Split(' ');
            Requests++;
            if (parts.Length < 2 || parts[0] != "GET")
            {
                Send(stream, "405 Method Not Allowed", "text/plain; charset=utf-8", "method not allowed\n");
                return;
            }
            string path = parts[1];
            int query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);
            lock (_lock) _local.CopyFrom(_shared);
            if (path == "/metrics")
            {
                var sb = new StringBuilder(4096);
                _local.WritePrometheus(sb);
                Send(stream, "200 OK", "text/plain; version=0.0.4; charset=utf-8", sb.ToString());
            }
            else if (path == "/healthz")
            {
                if (_local.Draining) Send(stream, "503 Service Unavailable", "text/plain; charset=utf-8", "draining\n");
                else Send(stream, "200 OK", "text/plain; charset=utf-8", "ok\n");
            }
            else
            {
                Send(stream, "404 Not Found", "text/plain; charset=utf-8", "not found\n");
            }
        }

        private static bool HasHeaderEnd(byte[] b, int length)
        {
            for (int i = 3; i < length; i++)
                if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') return true;
            return false;
        }

        private static void Send(NetworkStream stream, string status, string contentType, string body)
        {
            byte[] content = Encoding.UTF8.GetBytes(body);
            string head = "HTTP/1.1 " + status + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\r\nConnection: close\r\n\r\n";
            byte[] header = Encoding.ASCII.GetBytes(head);
            stream.Write(header, 0, header.Length);
            stream.Write(content, 0, content.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            if (_stopping) return;
            _stopping = true;
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
            }
            _thread.Join(ReadTimeoutMs * 2);
        }
    }
}
