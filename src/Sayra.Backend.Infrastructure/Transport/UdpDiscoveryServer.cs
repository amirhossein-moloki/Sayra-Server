using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sayra.Backend.Application.Abstractions.Transport;
using Sayra.Backend.Infrastructure.Configuration.Options;

namespace Sayra.Backend.Infrastructure.Transport
{
    public class UdpDiscoveryServer : IHostedService, IUdpDiscoveryServer, IDisposable
    {
        private readonly DiscoveryOptions _discoveryOptions;
        private readonly ILogger<UdpDiscoveryServer> _logger;
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private readonly object _lock = new();

        public UdpDiscoveryServer(
            IOptions<DiscoveryOptions> discoveryOptions,
            ILogger<UdpDiscoveryServer> logger)
        {
            _discoveryOptions = discoveryOptions?.Value ?? throw new ArgumentNullException(nameof(discoveryOptions));
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

                // For this stage, we do not implement the complete discovery protocol/responses,
                // but we establish the reception capability.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse or process UDP datagram from {RemoteEndPoint}. Service is resilient and will continue.", remoteEndPoint);
            }
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
