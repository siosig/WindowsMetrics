using System.Globalization;
using System.IO;
using System.Text;
using WindowsMetrics.Models;

namespace WindowsMetrics.Services.Parsing;

/// <summary>
/// A tolerant hand-rolled reader for the Prometheus text exposition format, extended to accept
/// the Prometheus 3 "UTF-8 name" syntax where label names (and the metric name itself) may be
/// double-quoted strings inside the label braces, e.g. LibreHardwareMonitor's own
/// <c>lhm_memory_temperature_celsius {"sensorName"="DIMM 0", ...} 45.4</c> output.
/// Malformed lines are skipped rather than throwing, so one bad sample never takes down a scrape.
/// See specs/004-windowsmetrics-github-publish/contracts/provider-metrics.md for the exact grammar
/// this was written against.
/// </summary>
public static class PrometheusTextParser
{
    public static List<FlatMetric> Parse(TextReader reader, ulong timeNano, IReadOnlyCollection<string>? namePrefixFilter = null)
    {
        var result = new List<FlatMetric>();
        var cumulativeTypes = new HashSet<string>(StringComparer.Ordinal);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0) continue;

            if (trimmed[0] == '#')
            {
                TryRecordTypeDirective(trimmed, cumulativeTypes);
                continue;
            }

            var sample = TryParseSample(trimmed);
            if (sample is null) continue;

            var (name, labels, value) = sample.Value;
            if (namePrefixFilter is not null &&
                !namePrefixFilter.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                continue;

            bool isCumulative = cumulativeTypes.Contains(name) || name.EndsWith("_total", StringComparison.Ordinal);
            result.Add(new FlatMetric(name, labels, value, timeNano, isCumulative));
        }

        return result;
    }

    // "# TYPE <name> <type>" — records names declared as counters. Any other '#' line
    // (HELP, unrecognized directives, comments) is silently ignored.
    private static void TryRecordTypeDirective(string line, HashSet<string> cumulativeTypes)
    {
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 && parts[0] == "#" && parts[1] == "TYPE" &&
            string.Equals(parts[3], "counter", StringComparison.OrdinalIgnoreCase))
        {
            cumulativeTypes.Add(parts[2]);
        }
    }

    private static (string Name, Dictionary<string, string> Labels, double Value)? TryParseSample(string line)
    {
        int i = 0;
        int n = line.Length;
        var labels = new Dictionary<string, string>();
        string name;

        void SkipWs()
        {
            while (i < n && (line[i] == ' ' || line[i] == '\t')) i++;
        }

        SkipWs();
        if (i >= n) return null;

        if (line[i] == '{')
        {
            // {"metric.name", "label"="value", ...} — Prometheus 3 quoted-name form.
            i++;
            SkipWs();
            if (i >= n || line[i] != '"') return null;
            var quotedName = ReadQuotedString(line, i);
            if (quotedName is null) return null;
            name = quotedName.Value.Text;
            i = quotedName.Value.NextIndex;
            SkipWs();
            if (i < n && line[i] == ',')
            {
                i++;
                if (!ReadLabelList(line, ref i, labels)) return null;
            }
            SkipWs();
            if (i >= n || line[i] != '}') return null;
            i++;
        }
        else
        {
            int start = i;
            while (i < n && line[i] != ' ' && line[i] != '\t' && line[i] != '{') i++;
            if (i == start) return null;
            name = line[start..i];

            SkipWs();
            if (i < n && line[i] == '{')
            {
                i++;
                if (!ReadLabelList(line, ref i, labels)) return null;
                SkipWs();
                if (i >= n || line[i] != '}') return null;
                i++;
            }
        }

        SkipWs();
        int valueStart = i;
        while (i < n && line[i] != ' ' && line[i] != '\t') i++;
        if (i == valueStart) return null;

        if (!TryParseValue(line[valueStart..i], out var value)) return null;

        // Any trailing content (an optional exemplar timestamp) is intentionally ignored.
        return (name, labels, value);
    }

    // Reads zero or more "name=\"value\"" pairs (name may be bare or quoted) separated by
    // commas, tolerating a single trailing comma before the caller-consumed closing '}'.
    private static bool ReadLabelList(string line, ref int i, Dictionary<string, string> labels)
    {
        int n = line.Length;

        while (true)
        {
            SkipWhitespace(line, ref i);
            if (i >= n) return false;
            if (line[i] == '}') return true;

            string labelName;
            if (line[i] == '"')
            {
                var quoted = ReadQuotedString(line, i);
                if (quoted is null) return false;
                labelName = quoted.Value.Text;
                i = quoted.Value.NextIndex;
            }
            else
            {
                int start = i;
                while (i < n && line[i] != '=' && line[i] != ' ' && line[i] != '\t') i++;
                if (i == start) return false;
                labelName = line[start..i];
            }

            SkipWhitespace(line, ref i);
            if (i >= n || line[i] != '=') return false;
            i++;
            SkipWhitespace(line, ref i);
            if (i >= n || line[i] != '"') return false;

            var quotedValue = ReadQuotedString(line, i);
            if (quotedValue is null) return false;
            i = quotedValue.Value.NextIndex;
            labels[labelName] = quotedValue.Value.Text;

            SkipWhitespace(line, ref i);
            if (i < n && line[i] == ',')
            {
                i++;
                SkipWhitespace(line, ref i);
                if (i < n && line[i] == '}') return true; // trailing comma
                continue;
            }
            return true;
        }
    }

    // Reads a double-quoted, backslash-escaped string starting at the opening quote.
    // Returns null if the string is never terminated (malformed line).
    private static (string Text, int NextIndex)? ReadQuotedString(string line, int openQuoteIndex)
    {
        int i = openQuoteIndex + 1;
        int n = line.Length;
        var sb = new StringBuilder();

        while (i < n)
        {
            char c = line[i];
            if (c == '"') return (sb.ToString(), i + 1);

            if (c == '\\' && i + 1 < n)
            {
                switch (line[i + 1])
                {
                    case '"': sb.Append('"'); i += 2; continue;
                    case '\\': sb.Append('\\'); i += 2; continue;
                    case 'n': sb.Append('\n'); i += 2; continue;
                    default: sb.Append(c); i += 1; continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return null; // unterminated
    }

    private static void SkipWhitespace(string line, ref int i)
    {
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
    }

    private static bool TryParseValue(string token, out double value)
    {
        switch (token)
        {
            case "NaN": value = double.NaN; return true;
            case "+Inf": value = double.PositiveInfinity; return true;
            case "-Inf": value = double.NegativeInfinity; return true;
            default: return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
