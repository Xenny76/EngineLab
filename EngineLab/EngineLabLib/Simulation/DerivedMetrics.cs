using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    public static class DerivedMetrics
    {
        public static double Displacement_L(EngineModel e)
            => Math.PI / 4.0 * Math.Pow(e.Bore_mm / 1000.0, 2) * (e.Stroke_mm / 1000.0) * e.Cylinders;
        public static double MeanPistonSpeed_mps(EngineModel e, int rpm)
            => 2.0 * (e.Stroke_mm / 1000.0) * rpm / 60.0;
    }
}