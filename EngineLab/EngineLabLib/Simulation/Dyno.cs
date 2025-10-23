using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    public sealed class DynoConfig
    {
        public int RpmStart { get; init; } = 1500;
        public int RpmStop { get; init; } = 7500;
        public int StepRpm { get; init; } = 100;
        public bool WheelBasis { get; init; } = true;
    }

    public readonly record struct DynoPoint(int Rpm, double TorqueNm, double Hp);

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
                double tq = a.TorqueNm + t * (b.TorqueNm - a.TorqueNm);
                dst.Points.Add(new DynoPoint(rpm, tq, tq * rpm / 5252.0));
            }
            return dst;
        }

        public (DynoPoint peakHp, DynoPoint peakTq) Peaks()
        {
            if (IsEmpty) return (default, default);
            var peakHp = Points[0]; var peakTq = Points[0];
            foreach (var p in Points) { if (p.Hp > peakHp.Hp) peakHp = p; if (p.TorqueNm > peakTq.TorqueNm) peakTq = p; }
            return (peakHp, peakTq);
        }

        public static double AvgTorqueIn(DynoCurve c, int rpmA, int rpmB)
        {
            if (c.IsEmpty) return 0;
            double area = 0; int lastRpm = c.Points[0].Rpm; double lastTq = c.Points[0].TorqueNm;
            foreach (var p in c.Points)
            {
                if (p.Rpm < rpmA) { lastRpm = p.Rpm; lastTq = p.TorqueNm; continue; }
                if (p.Rpm > rpmB) break;
                area += 0.5 * (p.TorqueNm + lastTq) * (p.Rpm - lastRpm);
                lastRpm = p.Rpm; lastTq = p.TorqueNm;
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
        public double PeakTqGain => CurrentPeakTq.TorqueNm - BaselinePeakTq.TorqueNm;
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
            var baseRaw = dyno.SimulatePull(baseline, cfg);
            var currRaw = dyno.SimulatePull(current, cfg);

            var baseRes = DynoCurve.Resample(baseRaw, cfg.RpmStart, cfg.RpmStop, cfg.StepRpm);
            var currRes = DynoCurve.Resample(currRaw, cfg.RpmStart, cfg.RpmStop, cfg.StepRpm);

            var delta = new DynoCurve();
            for (int i = 0; i < baseRes.Points.Count && i < currRes.Points.Count; i++)
            {
                var a = baseRes.Points[i]; var b = currRes.Points[i];
                double dtq = b.TorqueNm - a.TorqueNm;
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
                Metrics = new CompareMetrics { BaselinePeakHp = bHp, BaselinePeakTq = bTq, CurrentPeakHp = cHp, CurrentPeakTq = cTq, MidAvgTqGain_2500_4500 = mid }
            };
        }
    }
}