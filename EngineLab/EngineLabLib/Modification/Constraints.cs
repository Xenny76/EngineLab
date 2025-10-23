using System.Globalization;
using EngineLabLib.Models;

namespace EngineLabLib.Modification
{
    public sealed class PathConstraint
    {
        public string Path { get; init; } = "";
        public double? Min { get; init; }
        public double? Max { get; init; }
        public double Step { get; init; } = 1.0;
        public Func<EngineModel, bool>? IsEnabledWhen { get; init; }
    }

    public static class PathConstraints
    {
        public static readonly Dictionary<string, PathConstraint> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bore_mm"] = new() { Min = 60, Max = 110, Step = 0.1 },
            ["Stroke_mm"] = new() { Min = 50, Max = 120, Step = 0.1 },
            ["WOT_Lambda"] = new() { Min = 0.80, Max = 1.05, Step = 0.01 },
            ["Redline_RPM"] = new() { Min = 3000, Max = 9500, Step = 50 },
            ["RevLimit_RPM"] = new() { Min = 3200, Max = 9800, Step = 50 },
            ["PrimaryLength1_mm"] = new() { Min = 300, Max = 800, Step = 5 },
            ["PrimaryLength2_mm"] = new()
            {
                Min = 300,
                Max = 800,
                Step = 5,
                IsEnabledWhen = e => e.Header is HeaderLayout._421 or HeaderLayout.UEL
            },
            ["PrimaryID_mm"] = new() { Min = 30, Max = 45, Step = 1 },
            ["ThrottleDiameter_mm"] = new() { Min = 45, Max = 80, Step = 1 },
            ["RunnerLength_mm"] = new() { Min = 180, Max = 500, Step = 5 },
            ["Cam.LobeSeparationAngle_deg"] = new() { Min = 98, Max = 116, Step = 1 },
            ["Cam.IntakeDuration_deg050"] = new() { Min = 190, Max = 320, Step = 1 },
            ["Cam.ExhaustDuration_deg050"] = new() { Min = 190, Max = 320, Step = 1 },
            ["Cam.IntakeMaxLift_mm"] = new() { Min = 5, Max = 16, Step = 0.1 },
            ["Cam.ExhaustMaxLift_mm"] = new() { Min = 5, Max = 16, Step = 0.1 },
        };

        public static (object coerced, string? note) Clamp(string path, object raw, EngineModel cur)
        {
            if (!Map.TryGetValue(path, out var c)) return (raw, null);
            if (c.IsEnabledWhen is not null && !c.IsEnabledWhen(cur)) return (raw, "Disabled for current layout.");
            if (raw is IConvertible conv)
            {
                double v = Convert.ToDouble(conv, CultureInfo.InvariantCulture);
                if (c.Min is double min && v < min) return (min, $"Clamped to {min}");
                if (c.Max is double max && v > max) return (max, $"Clamped to {max}");
            }
            return (raw, null);
        }
    }
}