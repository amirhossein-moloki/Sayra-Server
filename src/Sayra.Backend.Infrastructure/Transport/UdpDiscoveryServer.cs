using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Contracts;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class UdpDiscoveryServer : IHostedService, IUdpDiscoveryServer, IDisposable
    {
        private readonly DiscoveryOptions _discoveryOptions;
        private readonly ServerOptions _serverOptions;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UdpDiscoveryServer> _logger;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private readonly object _lock = new();

        public UdpDiscoveryServer(
            IOptions<DiscoveryOptions> discoveryOptions,
            ILogger<UdpDiscoveryServer> logger)
            : this(discoveryOptions,
                   Microsoft.Extensions.Options.Options.Create(new ServerOptions()),
                   new ConfigurationBuilder().Build(),
                   logger)
        {
        }

        public UdpDiscoveryServer(
            IOptions<DiscoveryOptions> discoveryOptions,
            IOptions<ServerOptions> serverOptions,
            IConfiguration configuration,
            ILogger<UdpDiscoveryServer> logger)
        {
            _discoveryOptions = discoveryOptions?.Value ?? throw new ArgumentNullException(nameof(discoveryOptions));
            _serverOptions = serverOptions?.Value ?? throw new ArgumentNullException(nameof(serverOptions));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_discoveryOptions.Enabled)
            {
                _logger.LogInformation("UDP Discovery service is disabled in configuration.");
                return;
            }

            _logger.LogInformation("Starting UDP Discovery transport service...");

            lock (_lock)
            {
                if (_udpClient != null)
                {
                    _logger.LogWarning("UDP Discovery Service is already running.");
                    return;
                }

                _cts = new CancellationTokenSource();

                try
                {
                    // Bind to the configured UDP Port. Support dual-stack sockets or any address.
                    _udpClient = new UdpClient();
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryOptions.UdpPort));
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Failed to bind UdpClient to port {UdpPort}.", _discoveryOptions.UdpPort);
                    _udpClient?.Dispose();
                    _udpClient = null;
                    throw;
                }

                _logger.LogInformation("UDP Discovery Server successfully bound and listening on port {UdpPort}.", _discoveryOptions.UdpPort);

                _listenerTask = ListenForDatagramsAsync(_cts.Token);
            }

            await Task.CompletedTask;
        }

        private async Task ListenForDatagramsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // ReceiveUdpClientResult contains Buffer and RemoteEndPoint
                    var result = await _udpClient!.ReceiveAsync(cancellationToken);

                    _logger.LogDebug("Received UDP datagram from {RemoteEndPoint} ({Length} bytes).", result.RemoteEndPoint, result.Buffer.Length);

                    // Process received payload safely so invalid/malformed packets do not crash the service
                    ProcessDatagramSafely(result.Buffer, result.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error occurred in the UDP Discovery receive loop.");
                    }
                }
            }
        }

        private void ProcessDatagramSafely(byte[] buffer, IPEndPoint remoteEndPoint)
        {
            try
            {
                if (buffer == null || buffer.Length == 0)
                {
                    _logger.LogWarning("Received empty UDP payload from {RemoteEndPoint}.", remoteEndPoint);
                    return;
                }

                string payload = Encoding.UTF8.GetString(buffer);
                _logger.LogInformation("UDP Datagram received from {RemoteEndPoint}: {Payload}", remoteEndPoint, payload);

                // Parse payload as JSON
                using var jsonDoc = JsonDocument.Parse(payload);
                var root = jsonDoc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "DISCOVER_SAYRA_SERVER")
                {
                    _logger.LogWarning("Invalid UDP discovery packet type received from {RemoteEndPoint}.", remoteEndPoint);
                    return;
                }

                // Parse other fields if needed, like nonce or clientId
                string clientId = root.TryGetProperty("clientId", out var clientProp) ? clientProp.GetString() ?? "" : "";
                string nonce = root.TryGetProperty("nonce", out var nonceProp) ? nonceProp.GetString() ?? "" : "";

                // Retrieve master key
                var masterKey = _configuration["SAYRA_MASTER_KEY"];
                if (string.IsNullOrEmpty(masterKey))
                {
                    _logger.LogError("SAYRA_MASTER_KEY is missing or empty. Cannot process discovery response.");
                    return;
                }

                // Settle on Server Properties
                string serverId = _configuration["SAYRA_SERVER_ID"] ?? "SAYRA-CENTRAL-01";
                string serverName = _discoveryOptions.ServerName ?? "SAYRA Core Host";
                string ip = GetLocalIpAddress(remoteEndPoint);
                int tcpPort = _serverOptions.Port;
                int apiPort = tcpPort; // Or from some configuration, but the requirement lists tcpPort: 5000 and apiPort: 5000
                string version = "1.0.0"; // Or from some version configuration

                var now = DateTime.UtcNow;
                var timestampClean = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
                string timestampStr = timestampClean.ToString("yyyy-MM-ddTHH:mm:ssZ");

                // Signature calculation: HMAC-SHA256(MasterKey, serverId + "|" + ip + "|" + tcpPort + "|" + timestamp)
                string signatureInput = $"{serverId}|{ip}|{tcpPort}|{timestampStr}";
                byte[] signatureInputBytes = Encoding.UTF8.GetBytes(signatureInput);
                byte[] masterKeyBytes = Encoding.UTF8.GetBytes(masterKey);

                // High-performance zero-allocation static HMAC-SHA256 calculation
                byte[] signatureBytes = HMACSHA256.HashData(masterKeyBytes, signatureInputBytes);
                string signatureBase64 = Convert.ToBase64String(signatureBytes);

                // Prepare DiscoveryResponse
                var response = new DiscoveryResponse
                {
                    Type = "SAYRA_SERVER_RESPONSE",
                    ServerId = serverId,
                    ServerName = serverName,
                    Ip = ip,
                    TcpPort = tcpPort,
                    ApiPort = apiPort,
                    Version = version,
                    Timestamp = timestampClean,
                    Nonce = string.IsNullOrEmpty(nonce) ? Guid.NewGuid().ToString() : nonce,
                    Signature = signatureBase64
                };

                string responseJson = ProtocolSerialization.Serialize(response);
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);

                _udpClient!.Send(responseBytes, responseBytes.Length, remoteEndPoint);

                _logger.LogInformation("Sent SAYRA_SERVER_RESPONSE to {RemoteEndPoint} with IP {IP}.", remoteEndPoint, ip);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse malformed JSON UDP datagram from {RemoteEndPoint}. Service is resilient and will continue.", remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process UDP datagram from {RemoteEndPoint}. Service is resilient and will continue.", remoteEndPoint);
            }
        }

        private string GetLocalIpAddress(IPEndPoint remoteEndPoint)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(remoteEndPoint);
                if (socket.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    return localEndPoint.Address.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve outbound interface IP using connect routing lookup.");
            }

            return "127.0.0.1";
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping UDP Discovery transport service...");

            lock (_lock)
            {
                if (_udpClient == null)
                {
                    return;
                }

                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                try
                {
                    _udpClient.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while closing UdpClient.");
                }

                _udpClient.Dispose();
                _udpClient = null;
            }

            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask;
                }
                catch
                {
                    // Ignore listener tasks exceptions on cancel
                }
            }

            _cts?.Dispose();

            _logger.LogInformation("UDP Discovery transport service stopped cleanly.");
        }

        public void Dispose()
        {
            _cts?.Dispose();
            _udpClient?.Dispose();
        }
    }
}
