using System.Threading.Channels;

namespace ARIS1.Services.Email
{
    // In-memory hand-off from page requests to EmailBackgroundService. Only EmailLog ids travel through
    // the channel; the email itself is already persisted, so nothing is lost if the app restarts
    // (EmailBackgroundService re-queues any rows still marked Queued on startup).
    public class EmailQueue
    {
        private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(
            new UnboundedChannelOptions { SingleReader = true });

        public void Enqueue(int emailLogId) => _channel.Writer.TryWrite(emailLogId);

        public IAsyncEnumerable<int> ReadAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
