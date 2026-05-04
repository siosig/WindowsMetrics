using System.Text.Json.Serialization;

namespace OtlpMonitor.Models.Otlp;

public class OtlpExportRequest
{
    [JsonPropertyName("resourceMetrics")]
    public List<ResourceMetrics>? ResourceMetrics { get; set; }
}

public class ResourceMetrics
{
    [JsonPropertyName("resource")]
    public OtlpResource? Resource { get; set; }

    [JsonPropertyName("scopeMetrics")]
    public List<ScopeMetrics>? ScopeMetrics { get; set; }

    [JsonPropertyName("schemaUrl")]
    public string? SchemaUrl { get; set; }
}

public class OtlpResource
{
    [JsonPropertyName("attributes")]
    public List<KeyValue>? Attributes { get; set; }

    [JsonPropertyName("droppedAttributesCount")]
    public int? DroppedAttributesCount { get; set; }
}

public class ScopeMetrics
{
    [JsonPropertyName("scope")]
    public InstrumentationScope? Scope { get; set; }

    [JsonPropertyName("metrics")]
    public List<OtlpMetric>? Metrics { get; set; }

    [JsonPropertyName("schemaUrl")]
    public string? SchemaUrl { get; set; }
}

public class InstrumentationScope
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public class OtlpMetric
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("gauge")]
    public GaugeData? Gauge { get; set; }

    [JsonPropertyName("sum")]
    public SumData? Sum { get; set; }
}

public class GaugeData
{
    [JsonPropertyName("dataPoints")]
    public List<NumberDataPoint>? DataPoints { get; set; }
}

public class SumData
{
    [JsonPropertyName("dataPoints")]
    public List<NumberDataPoint>? DataPoints { get; set; }

    [JsonPropertyName("aggregationTemporality")]
    public int? AggregationTemporality { get; set; }

    [JsonPropertyName("isMonotonic")]
    public bool? IsMonotonic { get; set; }
}

public class NumberDataPoint
{
    [JsonPropertyName("attributes")]
    public List<KeyValue>? Attributes { get; set; }

    [JsonPropertyName("startTimeUnixNano")]
    public string? StartTimeUnixNano { get; set; }

    [JsonPropertyName("timeUnixNano")]
    public string? TimeUnixNano { get; set; }

    [JsonPropertyName("asDouble")]
    public double? AsDouble { get; set; }

    [JsonPropertyName("asInt")]
    public string? AsInt { get; set; }

    [JsonPropertyName("flags")]
    public uint? Flags { get; set; }

    public double GetValue()
    {
        if (AsDouble.HasValue) return AsDouble.Value;
        if (AsInt != null && long.TryParse(AsInt, out var i)) return (double)i;
        return 0.0;
    }

    public ulong GetTimeNano()
    {
        if (TimeUnixNano != null && ulong.TryParse(TimeUnixNano, out var t)) return t;
        return 0;
    }
}

public class KeyValue
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public AnyValue? Value { get; set; }
}

public class AnyValue
{
    [JsonPropertyName("stringValue")]
    public string? StringValue { get; set; }

    [JsonPropertyName("intValue")]
    public string? IntValue { get; set; }

    [JsonPropertyName("doubleValue")]
    public double? DoubleValue { get; set; }

    [JsonPropertyName("boolValue")]
    public bool? BoolValue { get; set; }

    public string AsString() => StringValue ?? IntValue ?? DoubleValue?.ToString() ?? BoolValue?.ToString() ?? string.Empty;
}
