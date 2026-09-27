using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace PhoneVr
{
    public struct PhoneSample
    {
        public float Alpha;
        public float Beta;
        public float Gamma;
        public float Screen;
        public float Trigger;
        public float Grip;
        public bool Primary;
        public bool Secondary;
        public bool Menu;
        public bool StickClick;
        public bool Recenter;
        public bool PointWithTop;
        public float StickX;
        public float StickY;
    }

    public sealed class PhoneVrServer : IDisposable
    {
        const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const string CertPassword = "phonevr";

        readonly object _gate = new object();
        readonly List<TcpClient> _clients = new List<TcpClient>();
        TcpListener _listener;
        Thread _thread;
        X509Certificate2 _cert;
        volatile bool _running;
        volatile bool _clientConnected;
        PhoneSample _latest;
        bool _hasSample;
        bool _recenter;

        public int Port { get; private set; }
        public string[] Urls { get; private set; } = Array.Empty<string>();
        public string Error { get; private set; }
        public Action<string> Log { get; set; }

        public bool ClientConnected => _clientConnected;

        public bool TryGetSample(out PhoneSample sample)
        {
            lock (_gate)
            {
                sample = _latest;
                return _hasSample;
            }
        }

        public bool ConsumeRecenter()
        {
            lock (_gate)
            {
                bool value = _recenter;
                _recenter = false;
                return value;
            }
        }

        public void Start(int port)
        {
            if (_running)
                return;

            IPAddress[] ips = LocalAddresses();
            _cert = CreateCertificate(ips);
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Urls = BuildUrls(ips, Port);
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Phone VR" };
            _thread.Start();
            Log?.Invoke("Phone VR listening on " + string.Join(", ", Urls));
        }

        public void Dispose()
        {
            _running = false;
            _clientConnected = false;
            try { _listener?.Stop(); } catch { }
            lock (_gate)
            {
                foreach (TcpClient client in _clients)
                {
                    try { client.Close(); } catch { }
                }
                _clients.Clear();
            }
            try { _cert?.Dispose(); } catch { }
        }

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch
                {
                    if (_running) continue;
                    break;
                }
                client.NoDelay = true;
                var worker = new Thread(() => Serve(client)) { IsBackground = true };
                worker.Start();
            }
        }

        void Serve(TcpClient client)
        {
            lock (_gate) _clients.Add(client);
            try
            {
                using NetworkStream raw = client.GetStream();
                using var ssl = new SslStream(raw, false);
                ssl.AuthenticateAsServer(_cert);
                if (!TryReadHttp(ssl, out string request))
                    return;
                if (request.StartsWith("GET /ws ", StringComparison.Ordinal))
                {
                    AcceptWebSocket(ssl, request);
                    ReadSocket(ssl);
                }
                else if (request.StartsWith("GET / ", StringComparison.Ordinal) || request.StartsWith("GET /?", StringComparison.Ordinal))
                {
                    WriteHttp(ssl, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(PhonePage.Html));
                }
                else
                {
                    WriteHttp(ssl, "404 Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"));
                }
            }
            catch (Exception ex)
            {
                if (_running)
                    Log?.Invoke("Phone connection closed: " + ex.Message);
            }
            finally
            {
                _clientConnected = false;
                lock (_gate) _clients.Remove(client);
                try { client.Close(); } catch { }
            }
        }

        void ReadSocket(Stream stream)
        {
            _clientConnected = true;
            var fragment = new MemoryStream();
            int fragmentOp = 0;
            while (_running)
            {
                if (!TryReadFrame(stream, out int opcode, out bool fin, out byte[] payload))
                    break;
                if (opcode == 0x8)
                    break;
                if (opcode == 0x9)
                {
                    WriteFrame(stream, 0xA, payload);
                    continue;
                }
                if (opcode == 0x1 || opcode == 0x0)
                {
                    if (opcode == 0x1)
                    {
                        fragment.SetLength(0);
                        fragmentOp = 0x1;
                    }
                    fragment.Write(payload, 0, payload.Length);
                    if (fin && fragmentOp == 0x1)
                    {
                        string text = Encoding.UTF8.GetString(fragment.ToArray());
                        if (TryParse(text, out PhoneSample sample))
                        {
                            lock (_gate)
                            {
                                _latest = sample;
                                _hasSample = true;
                                if (sample.Recenter)
                                    _recenter = true;
                            }
                        }
                    }
                }
            }
        }

        static bool TryParse(string text, out PhoneSample sample)
        {
            sample = default;
            string[] parts = text.Trim().Split(' ');
            if (parts.Length < 14)
                return false;
            if (!TryF(parts[0], out sample.Alpha) || !TryF(parts[1], out sample.Beta) ||
                !TryF(parts[2], out sample.Gamma) || !TryF(parts[3], out sample.Screen) ||
                !TryF(parts[4], out sample.Trigger) || !TryF(parts[5], out sample.Grip) ||
                !TryF(parts[9], out sample.StickX) || !TryF(parts[10], out sample.StickY))
                return false;
            sample.Primary = Flag(parts[6]);
            sample.Secondary = Flag(parts[7]);
            sample.Menu = Flag(parts[8]);
            sample.StickClick = Flag(parts[11]);
            sample.Recenter = Flag(parts[12]);
            sample.PointWithTop = Flag(parts[13]);
            return true;
        }

        static bool TryF(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static bool Flag(string text)
        {
            return TryF(text, out float value) && value > 0.5f;
        }

        static void AcceptWebSocket(Stream stream, string request)
        {
            string key = Header(request, "Sec-WebSocket-Key");
            string accept;
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(key + Magic));
                accept = Convert.ToBase64String(hash);
            }
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(response);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        static void WriteHttp(Stream stream, string status, string contentType, byte[] body)
        {
            string head =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        static bool TryReadHttp(Stream stream, out string request)
        {
            var buffer = new MemoryStream();
            var one = new byte[1];
            while (buffer.Length < 8192)
            {
                int n = stream.Read(one, 0, 1);
                if (n <= 0)
                    break;
                buffer.Write(one, 0, 1);
                if (buffer.Length >= 4)
                {
                    byte[] data = buffer.GetBuffer();
                    int len = (int)buffer.Length;
                    if (data[len - 4] == '\r' && data[len - 3] == '\n' && data[len - 2] == '\r' && data[len - 1] == '\n')
                    {
                        request = Encoding.ASCII.GetString(data, 0, len);
                        return true;
                    }
                }
            }
            request = null;
            return false;
        }

        static bool TryReadFrame(Stream stream, out int opcode, out bool fin, out byte[] payload)
        {
            opcode = 0;
            fin = false;
            payload = null;
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            if (b0 < 0 || b1 < 0)
                return false;
            fin = (b0 & 0x80) != 0;
            opcode = b0 & 0x0F;
            bool masked = (b1 & 0x80) != 0;
            long length = b1 & 0x7F;
            if (length == 126)
                length = (stream.ReadByte() << 8) | stream.ReadByte();
            else if (length == 127)
            {
                length = 0;
                for (int i = 0; i < 8; i++)
                    length = (length << 8) | (byte)stream.ReadByte();
            }
            if (length < 0 || length > 65536)
                return false;
            byte[] mask = masked ? ReadExact(stream, 4) : null;
            payload = ReadExact(stream, (int)length);
            if (payload == null || (masked && mask == null))
                return false;
            if (masked)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= mask[i % 4];
            }
            return true;
        }

        static void WriteFrame(Stream stream, int opcode, byte[] payload)
        {
            stream.WriteByte((byte)(0x80 | opcode));
            if (payload.Length < 126)
                stream.WriteByte((byte)payload.Length);
            else
            {
                stream.WriteByte(126);
                stream.WriteByte((byte)(payload.Length >> 8));
                stream.WriteByte((byte)payload.Length);
            }
            if (payload.Length > 0)
                stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        static byte[] ReadExact(Stream stream, int count)
        {
            var data = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(data, got, count - got);
                if (n <= 0)
                    return null;
                got += n;
            }
            return data;
        }

        static string Header(string request, string name)
        {
            string prefix = name + ":";
            foreach (string line in request.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(prefix.Length).Trim();
            }
            return "";
        }

        static IPAddress[] LocalAddresses()
        {
            var found = new List<IPAddress> { IPAddress.Loopback };
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                        continue;
                    if (address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                        continue;
                    found.Add(address);
                }
            }
            catch { }
            return found.ToArray();
        }

        static string[] BuildUrls(IPAddress[] ips, int port)
        {
            var urls = new string[ips.Length];
            for (int i = 0; i < ips.Length; i++)
                urls[i] = "https://" + ips[i] + ":" + port.ToString(CultureInfo.InvariantCulture) + "/";
            return urls;
        }

        X509Certificate2 CreateCertificate(IPAddress[] ips)
        {
            string dir = Path.Combine(Path.GetTempPath(), "phone-vr");
            Directory.CreateDirectory(dir);
            string pfx = Path.Combine(dir, "phone-vr.pfx");
            string script = Path.Combine(dir, "make-cert.ps1");
            if (File.Exists(pfx))
                File.Delete(pfx);
            var names = new StringBuilder("DNS=phone-vr.local");
            foreach (IPAddress ip in ips)
                names.Append("&IPAddress=").Append(ip);
            string ps = @"
$ErrorActionPreference = 'Stop'
$ext = '2.5.29.17={text}" + names + @"'
$cert = New-SelfSignedCertificate -Subject 'CN=Phone VR' -TextExtension $ext -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(2) -FriendlyName 'Phone VR'
$pwd = ConvertTo-SecureString '" + CertPassword + @"' -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath '" + pfx.Replace("'", "''") + @"' -Password $pwd | Out-Null
Remove-Item ('Cert:\CurrentUser\My\' + $cert.Thumbprint)
";
            File.WriteAllText(script, ps);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            var stderr = new StringBuilder();
            process.OutputDataReceived += (sender, args) => { };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    stderr.AppendLine(args.Data);
            };
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(); } catch { }
                throw new InvalidOperationException("Timed out creating the phone certificate.");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Could not create the phone certificate. " + stderr);
            byte[] raw = File.ReadAllBytes(pfx);
            return new X509Certificate2(raw, CertPassword, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        }
    }
}
