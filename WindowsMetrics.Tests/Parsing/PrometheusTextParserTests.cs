using System.IO;
using WindowsMetrics.Services.Parsing;

namespace WindowsMetrics.Tests.Parsing;

public class PrometheusTextParserTests
{
    private const ulong TimeNano = 1_700_000_000_000_000_000;

    [Fact]
    public void Parse_NoLabels_ReturnsNameAndValue()
    {
        var result = PrometheusTextParser.Parse(new StringReader("windows_system_processes 343"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("windows_system_processes", m.Name);
        Assert.Empty(m.Labels);
        Assert.Equal(343, m.Value);
    }

    [Fact]
    public void Parse_NormalLabels_ParsesLabelDictionary()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a{b=\"c\",d=\"e\"} 1"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("a", m.Name);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "c", ["d"] = "e" }, m.Labels);
    }

    [Fact]
    public void Parse_QuotedLabelNames_ParsesLabelDictionary()
    {
        // Prometheus 3 UTF-8 syntax: label names may be quoted strings, with optional
        // whitespace between the metric name and the opening brace.
        var result = PrometheusTextParser.Parse(new StringReader("a {\"b\"=\"c\", \"d\"=\"e\"} 1"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("a", m.Name);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "c", ["d"] = "e" }, m.Labels);
    }

    [Fact]
    public void Parse_QuotedMetricName_ParsesNameFromInsideBraces()
    {
        var result = PrometheusTextParser.Parse(new StringReader("{\"my.metric\", \"b\"=\"c\"} 1"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("my.metric", m.Name);
        Assert.Equal("c", m.Labels["b"]);
    }

    [Fact]
    public void Parse_EscapedLabelValue_UnescapesQuoteBackslashAndNewline()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a{b=\"x\\\"y\\\\z\\n\"} 1"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("x\"y\\z\n", m.Labels["b"]);
    }

    [Theory]
    [InlineData("a NaN")]
    public void Parse_NaN_ReturnsDoubleNaN(string line)
    {
        var result = PrometheusTextParser.Parse(new StringReader(line), TimeNano);
        Assert.True(double.IsNaN(Assert.Single(result).Value));
    }

    [Fact]
    public void Parse_PositiveInfinity_ReturnsPositiveInfinity()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a +Inf"), TimeNano);
        Assert.Equal(double.PositiveInfinity, Assert.Single(result).Value);
    }

    [Fact]
    public void Parse_NegativeInfinity_ReturnsNegativeInfinity()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a -Inf"), TimeNano);
        Assert.Equal(double.NegativeInfinity, Assert.Single(result).Value);
    }

    [Fact]
    public void Parse_ExponentialNotation_ParsesCorrectly()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a 3.4137534464e+10"), TimeNano);
        Assert.Equal(3.4137534464e+10, Assert.Single(result).Value);
    }

    [Fact]
    public void Parse_TrailingTimestamp_IsIgnoredButValueIsParsed()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a{b=\"c\"} 1 1700000000000"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal(1, m.Value);
        Assert.Equal("c", m.Labels["b"]);
    }

    [Fact]
    public void Parse_TrailingCommaAndExtraWhitespace_IsTolerated()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a{ b=\"c\", } 1"), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "c" }, m.Labels);
    }

    [Fact]
    public void Parse_TypeCounter_MarksSubsequentSampleCumulative()
    {
        var text = "# TYPE a counter\na 1";
        var result = PrometheusTextParser.Parse(new StringReader(text), TimeNano);
        Assert.True(Assert.Single(result).IsCumulative);
    }

    [Fact]
    public void Parse_TotalSuffixWithoutTypeDirective_IsTreatedAsCumulative()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a_total 1"), TimeNano);
        Assert.True(Assert.Single(result).IsCumulative);
    }

    [Fact]
    public void Parse_TypeGauge_IsNotCumulative()
    {
        var text = "# TYPE a gauge\na 1";
        var result = PrometheusTextParser.Parse(new StringReader(text), TimeNano);
        Assert.False(Assert.Single(result).IsCumulative);
    }

    [Fact]
    public void Parse_HelpBlankAndGarbageLines_AreIgnoredWithoutThrowing()
    {
        var text = "# HELP a some description\n\ngarbage line with no value\na 1";
        var ex = Record.Exception(() =>
        {
            var result = PrometheusTextParser.Parse(new StringReader(text), TimeNano);
            Assert.Single(result);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_MalformedBraces_SkipsLineWithoutThrowing()
    {
        var ex = Record.Exception(() =>
        {
            var result = PrometheusTextParser.Parse(new StringReader("a{invalid\nb 2"), TimeNano);
            var m = Assert.Single(result);
            Assert.Equal("b", m.Name);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_MultipleLines_ShareTheSameBatchTimeNano()
    {
        var result = PrometheusTextParser.Parse(new StringReader("a 1\nb 2\nc 3"), TimeNano);
        Assert.Equal(3, result.Count);
        Assert.All(result, m => Assert.Equal(TimeNano, m.TimeNano));
    }

    [Fact]
    public void Parse_NamePrefixFilter_ExcludesNonMatchingMetrics()
    {
        var text = "windows_cpu_time_total 1\nlhm_memory_temperature_celsius 45\nunrelated_metric 9";
        var result = PrometheusTextParser.Parse(new StringReader(text), TimeNano,
            namePrefixFilter: ["windows_cpu_", "lhm_memory_temperature_celsius"]);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, m => m.Name == "unrelated_metric");
    }

    [Fact]
    public void Parse_RealWorldLhmLine_ParsesQuotedNamesAndHardwareIdLabel()
    {
        var line = "lhm_memory_temperature_celsius {\"sensorName\"=\"DIMM 0\", \"hardwareId\"=\"/memory/dimm/0\"} 45.4375";
        var result = PrometheusTextParser.Parse(new StringReader(line), TimeNano);
        var m = Assert.Single(result);
        Assert.Equal("lhm_memory_temperature_celsius", m.Name);
        Assert.Equal("/memory/dimm/0", m.Labels["hardwareId"]);
        Assert.Equal(45.4375, m.Value);
    }
}
