using System.Threading.Channels;
using OtlpMonitor.Models.Display;

namespace OtlpMonitor.Services;

public class MetricsChannel
{
    private readonly Channel<MetricsSnapshot> _channel =
        Channel.CreateUnbounded<MetricsSnapshot>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<MetricsSnapshot> Writer => _channel.Writer;
    public ChannelReader<MetricsSnapshot> Reader => _channel.Reader;
}
