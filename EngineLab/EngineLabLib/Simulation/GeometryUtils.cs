using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    public static class GeometryUtils
    {
        public static double ComputeCR_FromGeometry(EngineModel e)
        {
            double B = e.Bore_mm / 1000.0, S = e.Stroke_mm / 1000.0;
            double Ab = Math.PI / 4.0 * B * B;
            double Vd_cyl = Ab * S;

            double Vch_cc = e.ChamberVolume_cc ?? 0.0;
            double Vdish_cc = e.PistonDishVolume_cc ?? 0.0;
            double deck_m = (e.DeckClearance_mm ?? 0.0) / 1000.0;
            double gbore_m = (e.HeadGasketBore_mm ?? e.Bore_mm) / 1000.0;
            double gthk_m = (e.HeadGasketThickness_mm ?? 0.0) / 1000.0;
            double Vgasket_m3 = Math.PI / 4.0 * gbore_m * gbore_m * gthk_m;

            double Vc_m3 = (Vch_cc - Vdish_cc) / 1e6 + Ab * deck_m + Vgasket_m3;
            if (Vc_m3 <= 0)
                throw new InvalidDataException($"Computed clearance volume <= 0 (Vc={Vc_m3}). Check chamber/dish/gasket/deck inputs.");
            return (Vd_cyl + Vc_m3) / Vc_m3;
        }
    }
}