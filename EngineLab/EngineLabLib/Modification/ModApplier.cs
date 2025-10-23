using System.Globalization;
using EngineLabLib.Models;

namespace EngineLabLib.Modification
{
    public static class ModApplier
    {
        public static EngineModel Apply(EngineModel src, ModPatch patch)
        {
            var dst = src;
            foreach (var s in patch.Sets)
            {
                switch (s.Path)
                {
                    // --- geometry
                    case "Bore_mm": dst = dst with { Bore_mm = ToDouble(s.Value) }; break;
                    case "Stroke_mm": dst = dst with { Stroke_mm = ToDouble(s.Value) }; break;

                    // --- compression
                    case "CompressionRatio":
                        dst = dst with { CompressionRatio = ToDouble(s.Value) }; break;
                    case "Toggles.CompressionBehavior":
                        dst = dst with
                        {
                            Toggles = dst.Toggles with
                            {
                                CompressionBehavior = Enum.Parse<CompressionBehavior>(s.Value!.ToString()!, true)
                            }
                        }; break;

                    // --- valvetrain
                    case "Cam.IntakeDuration_deg050":
                        dst = dst with { Cam = dst.Cam with { IntakeDuration_deg050 = ToDouble(s.Value) } }; break;
                    case "Cam.ExhaustDuration_deg050":
                        dst = dst with { Cam = dst.Cam with { ExhaustDuration_deg050 = ToDouble(s.Value) } }; break;
                    case "Cam.IntakeMaxLift_mm":
                        dst = dst with { Cam = dst.Cam with { IntakeMaxLift_mm = ToDouble(s.Value) } }; break;
                    case "Cam.ExhaustMaxLift_mm":
                        dst = dst with { Cam = dst.Cam with { ExhaustMaxLift_mm = ToDouble(s.Value) } }; break;
                    case "Cam.LobeSeparationAngle_deg":
                        dst = dst with { Cam = dst.Cam with { LobeSeparationAngle_deg = ToDouble(s.Value) } }; break;

                    // --- intake
                    case "RunnerLength_mm": dst = dst with { RunnerLength_mm = ToDouble(s.Value) }; break;
                    case "RunnerLengthShort_mm": dst = dst with { RunnerLengthShort_mm = ToNullableDouble(s.Value) }; break;
                    case "RunnerSwitchToShort_RPM":
                        dst = dst with { RunnerSwitchToShort_RPM = ToNullableInt(s.Value) }; break;
                    case "ThrottleDiameter_mm": dst = dst with { ThrottleDiameter_mm = ToDouble(s.Value) }; break;

                    // --- exhaust
                    case "Header":
                        dst = dst with { Header = Enum.Parse<HeaderLayout>(s.Value!.ToString()!, true) }; break;
                    case "PrimaryLength1_mm": dst = dst with { PrimaryLength1_mm = ToDouble(s.Value) }; break;
                    case "PrimaryLength2_mm": dst = dst with { PrimaryLength2_mm = ToNullableDouble(s.Value) }; break;
                    case "PrimaryID_mm": dst = dst with { PrimaryID_mm = ToDouble(s.Value) }; break;

                    // --- fuel / limits
                    case "WOT_Lambda": dst = dst with { WOT_Lambda = ToDouble(s.Value) }; break;
                    case "Redline_RPM": dst = dst with { Redline_RPM = ToInt(s.Value) }; break;
                    case "RevLimit_RPM": dst = dst with { RevLimit_RPM = ToInt(s.Value) }; break;

                    default: throw new NotSupportedException($"Unknown patch path '{s.Path}'.");
                }
            }
            return dst;
        }

        private static double ToDouble(object? v) => Convert.ToDouble(v, CultureInfo.InvariantCulture);
        private static double? ToNullableDouble(object? v) => v is null ? null : ToDouble(v);
        private static int ToInt(object? v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);
        private static int? ToNullableInt(object? v) => v is null ? null : ToInt(v);
    }

}