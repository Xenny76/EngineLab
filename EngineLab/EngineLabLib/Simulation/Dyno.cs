using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    public sealed class DynoConfig
    {
        public int RpmStart { get; init; } = 200;
        public int RpmStop { get; init; } = 7500;
        public int StepRpm { get; init; } = 100;
        public bool WheelBasis { get; init; } = true;

        /// <summary>
        /// Rev-limit headroom added to Redline for both baseline and current engines (derived, not a mod).
        /// </summary>
        public int RevHeadroomRpm { get; init; } = 200;
    }

    public readonly record struct DynoPoint(int Rpm, double TorqueLbFt, double Hp);

    public sealed class DynoCurve
    {
        public List<DynoPoint> Points { get; } = new(512);
        public bool IsEmpty => Points.Count == 0;

        public static DynoCurve Resample(DynoCurve src, int rpmStart, int rpmStop, int step)
        {
            var dst = new DynoCurve();
            if (src.IsEmpty) return dst;
            int n = src.Points.Count, j = 0;
            for (int rpm = rpmStart; rpm <= rpmStop; rpm += step)
            {
                while (j < n - 2 && src.Points[j + 1].Rpm < rpm) j++;
                var a = src.Points[Math.Max(0, j)];
                var b = src.Points[Math.Min(n - 1, j + 1)];
                double t = (b.Rpm == a.Rpm) ? 0 : (rpm - a.Rpm) / (double)(b.Rpm - a.Rpm);
                double tq = a.TorqueLbFt + t * (b.TorqueLbFt - a.TorqueLbFt);
                dst.Points.Add(new DynoPoint(rpm, tq, tq * rpm / 5252.0));
            }
            return dst;
        }

        /// <summary>
        /// Like Resample, but only emits points within the source’s actual range (no extrapolation).
        /// </summary>
        public static DynoCurve ResampleClamped(DynoCurve src, int rpmStart, int rpmStop, int step)
        {
            var dst = new DynoCurve();
            if (src.IsEmpty) return dst;

            int n = src.Points.Count;
            int srcMin = src.Points[0].Rpm;
            int srcMax = src.Points[^1].Rpm;

            int j = 0;
            for (int rpm = rpmStart; rpm <= rpmStop; rpm += step)
            {
                if (rpm < srcMin) continue;
                if (rpm > srcMax) break;

                while (j < n - 2 && src.Points[j + 1].Rpm < rpm) j++;
                var a = src.Points[j];
                var b = src.Points[j + 1];

                double t = (b.Rpm == a.Rpm) ? 0 : (rpm - a.Rpm) / (double)(b.Rpm - a.Rpm);
                double tq = a.TorqueLbFt + t * (b.TorqueLbFt - a.TorqueLbFt);
                dst.Points.Add(new DynoPoint(rpm, tq, tq * rpm / 5252.0));
            }
            return dst;
        }

        public (DynoPoint peakHp, DynoPoint peakTq) Peaks()
        {
            if (IsEmpty) return (default, default);
            var peakHp = Points[0]; var peakTq = Points[0];
            foreach (var p in Points) { if (p.Hp > peakHp.Hp) peakHp = p; if (p.TorqueLbFt > peakTq.TorqueLbFt) peakTq = p; }
            return (peakHp, peakTq);
        }

        public static double AvgTorqueIn(DynoCurve c, int rpmA, int rpmB)
        {
            if (c.IsEmpty) return 0;
            double area = 0; int lastRpm = c.Points[0].Rpm; double lastTq = c.Points[0].TorqueLbFt;
            foreach (var p in c.Points)
            {
                if (p.Rpm < rpmA) { lastRpm = p.Rpm; lastTq = p.TorqueLbFt; continue; }
                if (p.Rpm > rpmB) break;
                area += 0.5 * (p.TorqueLbFt + lastTq) * (p.Rpm - lastRpm);
                lastRpm = p.Rpm; lastTq = p.TorqueLbFt;
            }
            return area / Math.Max(1, rpmB - rpmA);
        }
    }

    public interface IDynoRunner
    {
        DynoCurve SimulatePull(EngineModel spec, DynoConfig cfg);
    }

    public sealed class CompareMetrics
    {
        public DynoPoint BaselinePeakHp { get; init; }
        public DynoPoint BaselinePeakTq { get; init; }
        public DynoPoint CurrentPeakHp { get; init; }
        public DynoPoint CurrentPeakTq { get; init; }
        public double PeakHpGain => CurrentPeakHp.Hp - BaselinePeakHp.Hp;
        public double PeakTqGain => CurrentPeakTq.TorqueLbFt - BaselinePeakTq.TorqueLbFt;
        public double MidAvgTqGain_2500_4500 { get; init; }
    }

    public sealed class CompareResult
    {
        public DynoCurve Baseline { get; init; } = default!;
        public DynoCurve Current { get; init; } = default!;
        public DynoCurve Delta { get; init; } = default!;
        public CompareMetrics Metrics { get; init; } = default!;
    }

    public static class DynoCompare
    {
        public static CompareResult Run(EngineModel baseline, EngineModel current, DynoConfig cfg, IDynoRunner dyno)
        {
            // Derive rev limit for both engines: Redline + headroom
            var baseEff = baseline with { RevLimit_RPM = Math.Max(baseline.Redline_RPM, 0) + cfg.RevHeadroomRpm };
            var currEff = current with { RevLimit_RPM = Math.Max(current.Redline_RPM, 0) + cfg.RevHeadroomRpm };

            // Simulate
            var baseRaw = dyno.SimulatePull(baseEff, cfg);
            var currRaw = dyno.SimulatePull(currEff, cfg);

            int start = cfg.RpmStart;
            int step = cfg.StepRpm;

            int baseMax = baseRaw.IsEmpty ? start : baseRaw.Points[^1].Rpm;
            int currMax = currRaw.IsEmpty ? start : currRaw.Points[^1].Rpm;

            // Plotting curves: each clamped to its own end
            var baseRes = DynoCurve.ResampleClamped(baseRaw, start, baseMax, step);
            var currRes = DynoCurve.ResampleClamped(currRaw, start, currMax, step);

            // Delta on overlap only
            int overlapMax = Math.Min(baseMax, currMax);
            var baseForDelta = DynoCurve.ResampleClamped(baseRaw, start, overlapMax, step);
            var currForDelta = DynoCurve.ResampleClamped(currRaw, start, overlapMax, step);

            var delta = new DynoCurve();
            int m = Math.Min(baseForDelta.Points.Count, currForDelta.Points.Count);
            for (int i = 0; i < m; i++)
            {
                var a = baseForDelta.Points[i];
                var b = currForDelta.Points[i];
                double dtq = b.TorqueLbFt - a.TorqueLbFt;
                delta.Points.Add(new DynoPoint(a.Rpm, dtq, dtq * a.Rpm / 5252.0));
            }

            var (bHp, bTq) = baseRes.Peaks();
            var (cHp, cTq) = currRes.Peaks();
            double mid = DynoCurve.AvgTorqueIn(currRes, 2500, 4500) - DynoCurve.AvgTorqueIn(baseRes, 2500, 4500);

            return new CompareResult
            {
                Baseline = baseRes,
                Current = currRes,
                Delta = delta,
                Metrics = new CompareMetrics
                {
                    BaselinePeakHp = bHp,
                    BaselinePeakTq = bTq,
                    CurrentPeakHp = cHp,
                    CurrentPeakTq = cTq,
                    MidAvgTqGain_2500_4500 = mid
                }
            };
        }
    }
}