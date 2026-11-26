using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BSourceNotifier.API.Tests;

// A real SMTP conversation, including the second EHLO after STARTTLS.
internal sealed class SmtpTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(30));
    private readonly X509Certificate2 _certificate;
    private readonly Task _session;
    private readonly bool _advertiseStartTls;
    private readonly bool _requireAuth;
    private readonly bool _rejectAuth;
    private readonly TaskCompletionSource<string> _message = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> Commands { get; } = new();
    public int Port { get; }
    public bool TlsEstablished { get; private set; }
    public bool Authenticated { get; private set; }
    public Task<string> Message => _message.Task;

    public SmtpTestServer(bool advertiseStartTls = true, bool requireAuth = false,
        bool rejectAuth = false, string certificate = "localhost")
    {
        var directory = Environment.GetEnvironmentVariable("SMTP_TEST_CERT_DIR")
            ?? throw new InvalidOperationException("Run bash tests/run-tests.sh to provision isolated SMTP test certificates.");
        _certificate = new X509Certificate2(Path.Combine(directory, certificate + ".pfx"), "");
        _advertiseStartTls = advertiseStartTls;
        _requireAuth = requireAuth;
        _rejectAuth = rejectAuth;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _session = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            Stream stream = client.GetStream();
            StreamReader reader = CreateReader(stream);
            StreamWriter writer = CreateWriter(stream);
            try
            {
                await writer.WriteLineAsync("220 localhost SMTP test server");
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var parts = line.Split(' ', 3);
                    var verb = parts[0].ToUpperInvariant();
                    Commands.Enqueue(verb);
                    switch (verb)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-localhost");
                            if (_advertiseStartTls && !TlsEstablished)
                                await writer.WriteLineAsync("250-STARTTLS");
                            await writer.WriteLineAsync("250 AUTH LOGIN");
                            break;
                        case "STARTTLS":
                            await writer.WriteLineAsync("220 Ready for TLS");
                            reader.Dispose();
                            writer.Dispose();
                            var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                            stream = tls;
                            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                            {
                                ServerCertificate = _certificate,
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                            }, _stop.Token);
                            TlsEstablished = true;
                            reader = CreateReader(stream);
                            writer = CreateWriter(stream);
                            break;
                        case "AUTH":
                            if (!TlsEstablished)
                                throw new InvalidOperationException("AUTH was sent before STARTTLS.");
                            if (parts.Length < 2 || !parts[1].Equals("LOGIN", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Unexpected authentication mechanism.");
                            string? username = parts.Length == 3 ? parts[2] : null;
                            if (username is null)
                            {
                                await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                                username = await reader.ReadLineAsync(_stop.Token);
                            }
                            await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                            var password = await reader.ReadLineAsync(_stop.Token);
                            Authenticated = !_rejectAuth && Decode(username) == "smtp-test-user" && Decode(password) == "smtp-test-password";
                            await writer.WriteLineAsync(Authenticated ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication rejected");
                            break;
                        case "MAIL":
                            await writer.WriteLineAsync(_requireAuth && !Authenticated ? "530 5.7.0 Authentication required" : "250 Sender OK");
                            break;
                        case "RCPT":
                            await writer.WriteLineAsync("250 Recipient OK");
                            break;
                        case "DATA":
                            if (!TlsEstablished || (_requireAuth && !Authenticated))
                                throw new InvalidOperationException("Message sent without required TLS/authentication.");
                            await writer.WriteLineAsync("354 End with a dot");
                            var body = new StringBuilder();
                            while (await reader.ReadLineAsync(_stop.Token) is { } data && data != ".")
                                body.AppendLine(data.StartsWith("..", StringComparison.Ordinal) ? data[1..] : data);
                            _message.TrySetResult(body.ToString());
                            await writer.WriteLineAsync("250 2.0.0 Message accepted");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 Bye");
                            return;
                        default:
                            throw new InvalidOperationException($"Unexpected SMTP command: {verb}");
                    }
                }
            }
            finally
            {
                reader.Dispose();
                writer.Dispose();
                await stream.DisposeAsync();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or AuthenticationException or IOException)
        {
            // Failed TLS and client cancellation are expected in negative scenarios.
        }
    }

    private static string Decode(string? value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? ""));
    private static StreamReader CreateReader(Stream stream) => new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
    private static StreamWriter CreateWriter(Stream stream) => new(stream, Encoding.ASCII, 1024, leaveOpen: true)
    {
        NewLine = "\r\n",
        AutoFlush = true
    };

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _session.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally
        {
            _certificate.Dispose();
            _stop.Dispose();
        }
    }
}
