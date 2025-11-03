using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    /// <summary>
    /// Simple spark-ignition WOT simulator with lb-ft output.
    /// Differences vs. your previous version:
    ///   • Adds a pressure-drop model across the throttle (orifice approximation)
    ///     to reduce manifold pressure at high demand, yielding a lower effective
    ///     intake density used for m_air_cycle (no PMEP double-counting).
    ///   • Public knobs: ThrottleCd, MinManifoldPressure_kPa.
    ///
    /// Pipeline:
    ///   VE(rpm) → (compute requested ṁ) → throttle ΔP → p_man → ρ_eff → m_air_cycle →
    ///   fuel → q_fuel → IMEP → (FMEP+PMEP) → BMEP → torque(lb-ft) → HP
    /// </summary>
    public sealed class SimulationModel : IDynoRunner
    {
        // Public knobs
        public double DrivetrainLossFraction { get; init; } = 0.15; // applied when cfg.WheelBasis = true
        public int InternalStepRpm { get; init; } = 50;             // internal grid (resampled by compare layer)

        /// <summary>Throttle discharge coefficient (typ. 0.6–0.7).</summary>
        public double ThrottleCd { get; init; } = 0.62;

        /// <summary>Lower bound on manifold absolute pressure (kPa) for numeric stability.</summary>
        public double MinManifoldPressure_kPa { get; init; } = 50.0;

        // Constants
        private const double R_air = 287.05;        // J/kg/K
        private const double Nm_to_LbFt = 0.737562149;
        private const double HpDivisor = 5252.0;    // HP = lb-ft * RPM / 5252

        public DynoCurve SimulatePull(EngineModel e, DynoConfig cfg)
        {
            var curve = new DynoCurve();

            // --- Geometry & state ------------------------------------------------
            double B_m = e.Bore_mm / 1000.0;
            double S_m = e.Stroke_mm / 1000.0;
            double Ab_m2 = Math.PI / 4.0 * B_m * B_m;
            double Vd_cyl_m3 = Ab_m2 * S_m;
            double Vd_total_m3 = Vd_cyl_m3 * e.Cylinders; // per engine cycle

            double p_amb_Pa = e.AmbientPressure_kPa * 1000.0;
            double T_intake_K = e.IntakeAirTemp_K;
            double rho_amb = p_amb_Pa / (R_air * T_intake_K); // ambient intake density (no losses)

            double CR = e.CompressionRatio ?? GeometryUtils.ComputeCR_FromGeometry(e);

            int rpmStart = Math.Max(1000, cfg.RpmStart);
            int rpmStop = Math.Min(cfg.RpmStop, e.RevLimit_RPM);
            int step = Math.Min(InternalStepRpm, Math.Max(25, cfg.StepRpm));

            for (int rpm = rpmStart; rpm <= rpmStop; rpm += step)
            {
                // Runner length selection (dual length)
                double runnerL_mm = e.RunnerSwitchToShort_RPM is int sw && e.RunnerLengthShort_mm is double Ls && rpm >= sw
                    ? Ls
                    : e.RunnerLength_mm;

                // --- VE (volumetric efficiency) ---------------------------------
                double ve = VE(e, rpm, runnerL_mm);

                // --- Throttle pressure drop → effective intake density ----------
                // Area of the throttle bore:
                double A_th_m2 = Math.PI / 4.0 * Math.Pow(e.ThrottleDiameter_mm / 1000.0, 2);

                // Requested mass flow WITHOUT throttle restriction (from ambient rho)
                double m_air_cycle_req_kg = rho_amb * Vd_total_m3 * ve;  // kg / cycle (engine total)
                double m_dot_req_kg_s = m_air_cycle_req_kg * (rpm / 120.0); // cycles/s = rpm/120

                // Two-pass estimate to avoid a full iteration: compute ΔP with ambient density,
                // get a first p_man and rho_eff; recompute with rho_eff; average the two ΔP values.
                double deltaP0_Pa = OrificeDeltaP_Pa(rho_amb, m_dot_req_kg_s, A_th_m2, ThrottleCd);
                double p_man0_Pa = Clamp(p_amb_Pa - deltaP0_Pa, MinManifoldPressure_kPa * 1000.0, p_amb_Pa);
                double rho_eff0 = p_man0_Pa / (R_air * T_intake_K);

                double m_air_cycle_req_kg_1 = rho_eff0 * Vd_total_m3 * ve;
                double m_dot_req_kg_s_1 = m_air_cycle_req_kg_1 * (rpm / 120.0);
                double deltaP1_Pa = OrificeDeltaP_Pa(rho_eff0, m_dot_req_kg_s_1, A_th_m2, ThrottleCd);

                double deltaP_Pa = 0.5 * (deltaP0_Pa + deltaP1_Pa);
                double p_man_Pa = Clamp(p_amb_Pa - deltaP_Pa, MinManifoldPressure_kPa * 1000.0, p_amb_Pa);
                double rho_intake_eff = p_man_Pa / (R_air * T_intake_K); // <-- use this for m_air_cycle

                // --- Air & fuel per cycle (engine total) ------------------------
                double m_air_cycle = rho_intake_eff * Vd_total_m3 * ve; // kg / engine cycle
                double afr = Math.Max(0.0001, e.AFR_Stoich * e.WOT_Lambda);
                double m_fuel_cycle = m_air_cycle / afr;                 // kg / cycle
                double q_fuel_cycle_J = m_fuel_cycle * e.Fuel_LHV_MJ_per_kg * 1e6;

                // --- Indicated efficiency & IMEP --------------------------------
                double eta_i = IndicatedEta(e, rpm, CR, ve, runnerL_mm);
                double imep_Pa = eta_i * q_fuel_cycle_J / Math.Max(1e-9, Vd_total_m3);

                // --- Losses: FMEP + PMEP ---------------------------------------
                // PMEP is *not* augmented with throttle ΔP here to avoid double-counting
                // (we already reduced ρ via p_man). Keep your mild intake baseline and exhaust effects.
                double fmep_kPa = FMEP_kPa(e, rpm);
                double pmep_kPa = PMEP_kPa(e, rpm);
                double bmep_Pa = imep_Pa - (fmep_kPa + pmep_kPa) * 1000.0;
                if (bmep_Pa < 0) bmep_Pa = 0;

                // --- Shaft torque (Nm → lb-ft), drivetrain loss if wheel basis --
                double tq_Nm = bmep_Pa * Vd_total_m3 / (4.0 * Math.PI);

                // Injector capacity cap (post-combustion energy limitation)
                double cap = InjectorFlowCapRatio(e, rpm, m_fuel_cycle);
                if (cap < 1.0) tq_Nm *= cap;

                double tq_lbft = tq_Nm * Nm_to_LbFt;
                if (cfg.WheelBasis) tq_lbft *= (1.0 - DrivetrainLossFraction);

                // limiter soft taper (visual nicety)
                if (rpm > e.Redline_RPM)
                {
                    int over = rpm - e.Redline_RPM;
                    int taper = Math.Max(1, e.SoftTaper_RPM);
                    tq_lbft *= Math.Max(0, 1.0 - over / (double)taper);
                }

                double hp = tq_lbft * rpm / HpDivisor;

                curve.Points.Add(new DynoPoint(rpm, tq_lbft, hp));
            }

            return curve;
        }

        // ====================== Core pieces =====================================

        /// <summary>
        /// Simple orifice ΔP estimate:
        ///   V = ṁ / (ρ · C_d · A),  ΔP = ½ ρ V²  =  0.5 * ṁ² / (ρ · C_d² · A²)
        /// </summary>
        private static double OrificeDeltaP_Pa(double rho, double m_dot_kg_s, double A_m2, double Cd)
        {
            if (rho <= 0 || A_m2 <= 0 || Cd <= 0) return 0;
            double denom = rho * Cd * Cd * A_m2 * A_m2;
            return 0.5 * (m_dot_kg_s * m_dot_kg_s) / denom;
        }

        private static double VE(EngineModel e, int rpm, double runnerLength_mm)
        {
            // Cam influence: duration & LSA shape where VE peaks
            double dur = 0.5 * (e.Cam.IntakeDuration_deg050 + e.Cam.ExhaustDuration_deg050);
            double lsa = e.Cam.LobeSeparationAngle_deg;

            double rpm_cam_peak = 4000 + 30.0 * (dur - 220.0) - 40.0 * (lsa - 110.0);

            // Intake tuned length (very rough first-order guidance)
            double L_m = Math.Max(0.15, runnerLength_mm / 1000.0);
            double rpm_intake_peak = 2000.0 / L_m;

            // VVT (intake advance tends to lower the VE peak rpm slightly)
            double vvt_int = VVT_IntakeDeg(e, rpm);
            rpm_cam_peak *= (1.0 - 0.003 * vvt_int);

            // Blend & width
            double rpm_peak = 0.55 * rpm_intake_peak + 0.45 * rpm_cam_peak;
            double sigma = Math.Max(500.0, 0.28 * rpm_peak);

            // valve curtain area vs bore area (nudges VE max)
            int valvesInt = Math.Max(1, e.ValvesPerCylinder / 2);
            double curtain_m2 = Math.PI * (e.IntakeValveDiameter_mm / 1000.0) *
                                (e.Cam.IntakeMaxLift_mm / 1000.0) * valvesInt;
            double boreArea_m2 = Math.PI / 4.0 * Math.Pow(e.Bore_mm / 1000.0, 2);
            double areaRatio = curtain_m2 / Math.Max(1e-6, boreArea_m2); // ~0.2–0.6 typical NA

            double veMaxBase = Clamp(0.80 + 0.15 * (areaRatio - 0.35), 0.78, 1.12);

            // Optional head flow ceiling (if provided, acts as a soft cap)
            double veCap = VeCeilingFromHeadFlow(e, rpm, L_m);
            if (veCap > 0) veMaxBase = Math.Min(veMaxBase, veCap);

            double veMin = 0.70;

            double g = Math.Exp(-0.5 * Math.Pow((rpm - rpm_peak) / sigma, 2));
            double ve = veMin + (veMaxBase - veMin) * g;

            // LSA tweak: narrower lsa helps mid, hurts very top a touch
            ve *= 1.0 + 0.004 * (110.0 - lsa);
            ve *= TopEndTrim(rpm, rpm_peak, lsa);

            // Resonance features
            double width = Math.Max(200.0, e.Resonance.FeatureWidth_RPM);
            // intake bump near tuned rpm
            double bumpI = e.Resonance.IntakeBumpGain_0to1 *
                           Math.Exp(-0.5 * Math.Pow((rpm - rpm_intake_peak) / width, 2));
            ve *= (1.0 + bumpI);

            // exhaust bump & dip
            double rpm_exh_peak = ExhaustPeakRpm(e);
            double bumpE = e.Resonance.ExhaustBumpGain_0to1 *
                           Math.Exp(-0.5 * Math.Pow((rpm - rpm_exh_peak) / width, 2));
            ve *= (1.0 + bumpE);

            if (e.Header is HeaderLayout._421 or HeaderLayout.UEL)
            {
                double L2_m = (e.PrimaryLength2_mm ?? e.PrimaryLength1_mm) / 1000.0;
                double rpm_dip = 2000.0 / Math.Max(0.15, L2_m);
                double dipE = e.Resonance.ExhaustDipGain_0to1 *
                              Math.Exp(-0.5 * Math.Pow((rpm - rpm_dip) / width, 2));
                ve *= (1.0 - dipE);
            }

            return Clamp(ve, 0.55, 1.15);
        }

        private static double VeCeilingFromHeadFlow(EngineModel e, int rpm, double L_intake_m)
        {
            if (!e.Toggles.UseHeadFlowPointsWhenAvailable) return -1;
            if (e.HeadFlow_Intake_CFM28 is null || e.HeadFlow_Intake_CFM28.Count == 0) return -1;

            // crude ceiling: use peak CFM @ 28" to cap VE at high rpm
            double cfm = e.HeadFlow_Intake_CFM28.Max(fp => fp.CFM_28); // per valve equiv
            // Convert to m^3/s at 28" H2O: 1 CFM ≈ 0.000471947 m^3/s
            double m3_s = cfm * 0.000471947 * (e.ValvesPerCylinder / 2.0) * e.Cylinders;
            // theoretical flow needed for VE=1 at RPM:
            // volume per second at 100% VE (4-stroke cycles/s = rpm/120): Vd_total * rpm/120
            // We turn this into a cap factor. Keep it gentle (soft ceiling).
            double vol_s_at_ve1 = (Math.PI / 4.0) * Math.Pow(e.Bore_mm / 1000.0, 2) * (e.Stroke_mm / 1000.0) * e.Cylinders * (rpm / 120.0);
            if (vol_s_at_ve1 <= 0) return -1;
            double cap = 0.95 * (m3_s / vol_s_at_ve1); // 95% to leave headroom
            return Clamp(cap, 0.75, 1.10);
        }

        private static double VVT_IntakeDeg(EngineModel e, int rpm)
        {
            if (e.Vvt is null || e.Vvt.RpmSchedule is null || e.Vvt.RpmSchedule.Count == 0)
                return 0;

            var keys = e.Vvt.RpmSchedule.Keys.ToArray();
            int i = Array.BinarySearch(keys, rpm);
            if (i >= 0) return e.Vvt.RpmSchedule[keys[i]].Intake;
            i = ~i;
            if (i <= 0) return e.Vvt.RpmSchedule[keys[0]].Intake;
            if (i >= keys.Length) return e.Vvt.RpmSchedule[keys[^1]].Intake;
            int lo = keys[i - 1], hi = keys[i];
            double t = (rpm - lo) / (double)(hi - lo);
            var a = e.Vvt.RpmSchedule[lo].Intake;
            var b = e.Vvt.RpmSchedule[hi].Intake;
            return a + t * (b - a);
        }

        private static double ExhaustPeakRpm(EngineModel e)
        {
            double L1_m = Math.Max(0.15, e.PrimaryLength1_mm / 1000.0);
            return 2000.0 / L1_m;
        }

        private static double TopEndTrim(int rpm, double rpmPeak, double lsa)
        {
            // very high RPM trim (narrow LSA hurts top-end more)
            double over = Math.Max(0.0, rpm - rpmPeak);
            double k = 1e-7 * Math.Pow(Math.Max(0, 112 - lsa), 2);
            return Math.Max(0.88, 1.0 - k * over * over);
        }

        private static double IndicatedEta(EngineModel e, int rpm, double CR, double ve, double runnerL_mm)
        {
            // Otto-cycle efficiency vs CR
            double gamma = 1.32; // effective ratio for burned gas
            double eta_otto = 1.0 - Math.Pow(1.0 / Math.Max(1.01, CR), gamma - 1.0);

            // lambda penalty at WOT (rich -> lower)
            double lam = e.WOT_Lambda;
            double f_lambda = 1.0 - 0.50 * Math.Max(0.0, 1.0 - lam); // ~6% loss at λ=0.88

            // speed shaping about VE peak (blend of intake/exhaust peaks)
            double rpm_intake_peak = 2000.0 / Math.Max(0.15, runnerL_mm / 1000.0);
            double rpm_exh_peak = ExhaustPeakRpm(e);
            double rpm_peak = 0.5 * (rpm_intake_peak + rpm_exh_peak);
            double dev = (rpm - rpm_peak) / Math.Max(1000.0, rpm_peak);
            double f_speed = Clamp(1.0 - 0.18 * dev * dev, 0.72, 1.0);

            // mild VE coupling
            double f_ve = Clamp(0.75 + 0.4 * (ve - 0.8), 0.70, 1.02);

            return Clamp(eta_otto * f_lambda * f_speed * f_ve, 0.22, 0.42);
        }

        private static double FMEP_kPa(EngineModel e, int rpm)
        {
            double Up = DerivedMetrics.MeanPistonSpeed_mps(e, rpm); // m/s
            var f = e.Friction;
            return f.A_kPa + f.B_kPa_per_mps * Up + f.C_kPa_per_mps2 * Up * Up;
        }

        private static double PMEP_kPa(EngineModel e, int rpm)
        {
            // Exhaust backpressure above ambient
            double extra_kPa = 0.0;
            if (e.ExhaustBackpressure_kPa_ByRPM is { Count: > 0 })
                extra_kPa = LerpMap(e.ExhaustBackpressure_kPa_ByRPM, rpm);
            else if (e.CatBackpressure_kPa is double c)
                extra_kPa = c;

            // Intake pumping at WOT baseline (~8 kPa) + fraction of exhaust extra.
            // We do NOT add throttle ΔP here (we already modeled it via reduced rho).
            return 8.0 + 0.6 * Math.Max(0.0, extra_kPa);
        }

        private static double InjectorFlowCapRatio(EngineModel e, int rpm, double m_fuel_cycle_kg)
        {
            if (e.InjectorFlow_cc_per_min is not double ccpm || e.InjectorsPerCylinder <= 0)
                return 1.0;

            double injCount = e.Cylinders * e.InjectorsPerCylinder;

            // convert cc/min at duty to kg/s
            double fuelDensity = e.Fuel switch
            {
                FuelType.E85 => 0.79, // kg/L
                _ => 0.745
            };
            double kg_s_per_injector = (ccpm / 1000.0) * e.InjectorDutyLimit_0to1 * (1.0 / 60.0) * fuelDensity;
            double cap_kg_s = injCount * kg_s_per_injector;

            // required kg/s (4-stroke cycles/s = rpm / 120)
            double req_kg_s = m_fuel_cycle_kg * (rpm / 120.0);
            return Clamp(cap_kg_s / Math.Max(1e-9, req_kg_s), 0.0, 1.0);
        }

        private static double LerpMap(SortedDictionary<int, double> map, int x)
        {
            var keys = map.Keys.ToArray();
            int i = Array.BinarySearch(keys, x);
            if (i >= 0) return map[keys[i]];
            i = ~i;
            if (i <= 0) return map[keys[0]];
            if (i >= keys.Length) return map[keys[^1]];
            int lo = keys[i - 1], hi = keys[i];
            double t = (x - lo) / (double)(hi - lo);
            return map[lo] + t * (map[hi] - map[lo]);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}