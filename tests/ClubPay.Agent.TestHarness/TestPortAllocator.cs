using System.Net;
using System.Net.Sockets;

namespace ClubPay.Agent.TestHarness;

/// <summary>
/// Race-reduced ephemeral-port picker for tests that must pre-compute a port before starting an
/// HttpListener-based server (HttpListener has no "OS picks the port" mode, unlike Kestrel — see
/// FakeControllerServer, which no longer needs this at all). A process-wide lock removes the
/// intra-process half of the classic probe-then-release-then-bind race; it cannot remove the
/// inter-process half (a different process, or lingering TIME_WAIT, grabbing the port in the gap),
/// which is why ControllerHubService.StartAsync additionally retries its own bind.
/// </summary>
public static class TestPortAllocator
{
    private static readonly object Gate = new();

    public static int GetFreePort()
    {
        lock (Gate)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
