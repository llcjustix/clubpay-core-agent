using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class BillingEventReporterServiceTests
{
    private static BillingEventReporterService BuildSut(HttpMessageHandler handler, string baseUrl = "http://localhost:9090") =>
        new(handler, baseUrl, "test-token", "pc-001", NullLogger<BillingEventReporterService>.Instance);

    [Fact]
    public async Task ReportSessionStartedAsync_WhenServerReturns200_CompletesSuccessfully()
    {
        var handler = new StaticResponseHandler(HttpStatusCode.OK);
        using var sut = BuildSut(handler);

        await sut.ReportSessionStartedAsync(Guid.NewGuid());

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ReportPcStatusChangedAsync_WhenNetworkFails_RetriesThreeTimesAndSwallowsException()
    {
        var handler = new AlwaysThrowHandler();
        using var sut = BuildSut(handler);

        var ex = await Record.ExceptionAsync(() => sut.ReportPcStatusChangedAsync("Active"));

        Assert.Null(ex);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ReportCommandFailedAsync_WhenCancelled_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        var handler = new BlockUntilCancelledHandler(cts);
        using var sut = BuildSut(handler);

        var ex = await Record.ExceptionAsync(() => sut.ReportCommandFailedAsync("sessions/start", "test", cts.Token));

        Assert.Null(ex);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private sealed class StaticResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class AlwaysThrowHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("Simulated network failure");
        }
    }

    private sealed class BlockUntilCancelledHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cts.CancelAfter(50);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
