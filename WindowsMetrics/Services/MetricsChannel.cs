using System.Threading.Channels;
using WindowsMetrics.Models.Display;

namespace WindowsMetrics.Services;

public class MetricsChannel
{
    private readonly Channel<MetricsSnapshot> _channel =
        Channel.CreateUnbounded<MetricsSnapshot>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<MetricsSnapshot> Writer => _channel.Writer;
    public ChannelReader<MetricsSnapshot> Reader => _channel.Reader;
}
