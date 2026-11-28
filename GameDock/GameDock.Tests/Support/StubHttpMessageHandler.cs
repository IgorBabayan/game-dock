using System.Net;

namespace GameDock.Tests.Support;

/// <summary>Answers HTTP requests from a queue of canned responses (no network), recording every request.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public StubHttpMessageHandler Enqueue(HttpStatusCode status, string? body = null) =>
        Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty) });

    public StubHttpMessageHandler EnqueueBytes(HttpStatusCode status, byte[] body) =>
        Enqueue(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });

    public StubHttpMessageHandler EnqueueException(Exception exception) =>
        Enqueue(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (_responses.Count == 0)
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");

        return Task.FromResult(_responses.Dequeue()(request));
    }
}
