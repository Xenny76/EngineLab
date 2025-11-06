using EngineLabLib.Models;

namespace EngineLabLib.Simulation
{
    /// <summary>
    /// Spark-ignition WOT simulator with lb-ft output.
    /// - VE(rpm) from cam/valve/runner/header (+ VVT & resonance).
    /// - Throttle pressure drop (ThrottleDiameter_mm) reduces effective VE at high flow.
    /// - Exhaust primary diameter (PrimaryID_mm):
    ///     * Adds primary friction (kPa) to backpressure (stronger coefficients; header layout factor).
    ///     * Adds an exhaust-choke factor that reduces VE at high RPM when primaries are small.
    /// - Indicated efficiency from CR (Otto) with lambda & speed shaping.
    /// - Losses: FMEP + PMEP (PMEP now counts ALL extra backpressure).
    /// - Optional injector capacity cap.
    /// Returns torque in lb-ft; HP = T(lb-ft) * RPM / 5252.
    /// </summary>
    public sealed class SimulationModel : IDynoRunner
    {
        // Public knobs
        public double DrivetrainLossFraction { get; init; } = 0.15; // applied when cfg.WheelBasis = true
        public int InternalStepRpm { get; init; } = 50;             // internal grid (resampled by compare layer)

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

            // Ambient density used as a base; throttle drop is applied via VE multiplier
            double rho_amb = (e.AmbientPressure_kPa * 1000.0) / (R_air * e.IntakeAirTemp_K);
            double P_amb_kPa = e.AmbientPressure_kPa;

            double CR = e.CompressionRatio ?? GeometryUtils.ComputeCR_FromGeometry(e);

            int rpmStart = Math.Max(1000, cfg.RpmStart);
            int rpmStop = cfg.RpmStop; // compare layer derives rev limit
            int step = Math.Min(InternalStepRpm, Math.Max(25, cfg.StepRpm));

            for (int rpm = rpmStart; rpm <= rpmStop; rpm += step)
            {
                // Runner length selection (dual length)
                double runnerL_mm = e.RunnerSwitchToShort_RPM is int sw && e.RunnerLengthShort_mm is double Ls && rpm >= sw
                    ? Ls
                    : e.RunnerLength_mm;

                // ---- 1) VE core (no throttle/choke yet) ------------------------
                double ve0 = VE(e, rpm, runnerL_mm);

                // ---- 2) Throttle pressure drop → manifold pressure ratio -------
                double pRatio = ThrottlePressureRatio(e, rpm, ve0, Vd_total_m3, P_amb_kPa, rho_amb);

                // ---- 3) Exhaust choke factor from primary gas velocity ---------
                double vPrim = PrimaryGasVelocity_mps(e, rpm, ve0, Vd_total_m3, rho_amb);
                double fExhChoke = ExhaustChokeFactorFromVelocity(e, rpm, vPrim);

                // Effective VE
                double ve = Clamp(ve0 * pRatio * fExhChoke, 0.40, 1.15);

                // ---- 4) Air & fuel per cycle (engine total) --------------------
                double m_air_cycle = rho_amb * Vd_total_m3 * ve; // kg / engine cycle
                double afr = Math.Max(0.0001, e.AFR_Stoich * e.WOT_Lambda);
                double m_fuel_cycle = m_air_cycle / afr;            // kg / cycle
                double q_fuel_cycle_J = m_fuel_cycle * e.Fuel_LHV_MJ_per_kg * 1e6;

                // ---- 5) Indicated efficiency & IMEP ---------------------------
                double eta_i = IndicatedEta(e, rpm, CR, ve, runnerL_mm);
                double imep_Pa = eta_i * q_fuel_cycle_J / Math.Max(1e-9, Vd_total_m3);

                // ---- 6) Losses: FMEP + PMEP (+ primary friction) --------------
                double fmep_kPa = FMEP_kPa(e, rpm);

                // Mass flow (kg/s) for exhaust friction estimate
                double m_dot_air = m_air_cycle * (rpm / 120.0);
                double primaryFric_kPa = ExhaustPrimaryFriction_kPa(e, rpm, m_dot_air);

                double pmep_kPa = PMEP_kPa(e, rpm, primaryFric_kPa);
                double bmep_Pa = imep_Pa - (fmep_kPa + pmep_kPa) * 1000.0;
                if (bmep_Pa < 0) bmep_Pa = 0;

                // ---- 7) Shaft torque (Nm → lb-ft), drivetrain loss ------------
                double tq_Nm = bmep_Pa * Vd_total_m3 / (4.0 * Math.PI);
                double cap = InjectorFlowCapRatio(e, rpm, m_fuel_cycle);
                if (cap < 1.0) tq_Nm *= cap;

                double tq_lbft = tq_Nm * Nm_to_LbFt;
                if (cfg.WheelBasis) tq_lbft *= (1.0 - DrivetrainLossFraction);

                // limiter soft taper (visual only) — compare layer clamps to rev limit already
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

        private static double VE(EngineModel e, int rpm, double runnerLength_mm)
        {
            // Cam influence: duration & LSA shape where VE peaks
            double durInt = e.Cam.IntakeDuration_deg050;
            double durExh = e.Cam.ExhaustDuration_deg050;
            double durAvg = 0.5 * (durInt + durExh);
            double lsa = e.Cam.LobeSeparationAngle_deg;

            double rpm_cam_peak = 4000 + 30.0 * (durAvg - 220.0) - 40.0 * (lsa - 110.0);

            // Intake tuned length
            double L_m = Math.Max(0.15, runnerLength_mm / 1000.0);
            double rpm_intake_peak = 2000.0 / L_m;

            // VVT (intake advance tends to lower the VE peak rpm slightly)
            double vvt_int = VVT_IntakeDeg(e, rpm);
            rpm_cam_peak *= (1.0 - 0.003 * vvt_int);

            // Blend & width
            double rpm_peak = 0.55 * rpm_intake_peak + 0.45 * rpm_cam_peak;
            double sigma = Math.Max(500.0, 0.28 * rpm_peak);

            // ---- Valve curtain areas (intake + exhaust) -----------------------
            int valvesInt = Math.Max(1, e.ValvesPerCylinder / 2);
            int valvesExh = Math.Max(1, e.ValvesPerCylinder / 2);

            double di_m = e.IntakeValveDiameter_mm / 1000.0;
            double li_m = e.Cam.IntakeMaxLift_mm / 1000.0;

            double de_m = e.ExhaustValveDiameter_mm / 1000.0;
            double le_m = e.Cam.ExhaustMaxLift_mm / 1000.0;

            double curtainInt_m2 = Math.PI * di_m * li_m * valvesInt;
            double curtainExh_m2 = Math.PI * de_m * le_m * valvesExh;

            double boreArea_m2 = Math.PI / 4.0 * Math.Pow(e.Bore_mm / 1000.0, 2);
            double areaIntRatio = curtainInt_m2 / Math.Max(1e-9, boreArea_m2); // ~0.20–0.60
            double areaExhRatio = curtainExh_m2 / Math.Max(1e-9, boreArea_m2); // ~0.16–0.50

            // Intake curtain nudges VE max
            double veMaxBase = Clamp(0.80 + 0.15 * (areaIntRatio - 0.35), 0.78, 1.12);

            // Optional head flow ceiling (soft cap)
            double veCap = VeCeilingFromHeadFlow(e, rpm, L_m);
            if (veCap > 0) veMaxBase = Math.Min(veMaxBase, veCap);

            double veMin = 0.70;

            double g = Math.Exp(-0.5 * Math.Pow((rpm - rpm_peak) / sigma, 2));
            double ve = veMin + (veMaxBase - veMin) * g;

            // LSA tweak: narrower lsa helps mid, hurts very top a touch
            ve *= 1.0 + 0.004 * (110.0 - lsa);
            ve *= TopEndTrim(rpm, rpm_peak, lsa);

            // ---- Scavenging gain from exhaust curtain area & overlap -----------
            double overlap_deg = Math.Max(0.0, durInt + durExh - 2.0 * lsa);
            double overlapIndex = Clamp(overlap_deg / 60.0, 0.0, 2.0);    // 0..~2
            double exhAreaBenefit = Clamp((areaExhRatio - 0.22) / 0.18, 0.0, 1.0);
            double abovePeak = Clamp((rpm - rpm_peak) / (2.0 * sigma), 0.0, 1.0);
            double scavGain = 1.0 + 0.025 * overlapIndex * exhAreaBenefit * abovePeak;
            ve *= scavGain;

            // ---- Resonance features -------------------------------------------
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

        /// <summary>
        /// Throttle pressure ratio Pman/Pamb in [0.70 .. 1.0] from throttle body pressure drop.
        /// ΔP ≈ K * 0.5 * ρ * v^2 with v = Vdot / Ath and Vdot ≈ Vd_total * VE * rpm / 120.
        /// </summary>
        private static double ThrottlePressureRatio(EngineModel e, int rpm, double ve0, double Vd_total_m3, double P_amb_kPa, double rho_amb)
        {
            double D_th_m = Math.Max(0.010, e.ThrottleDiameter_mm / 1000.0); // >=10 mm to avoid div-by-zero
            double A_th_m2 = Math.PI * 0.25 * D_th_m * D_th_m;

            double Vdot_m3_s = Vd_total_m3 * Clamp(ve0, 0.2, 1.2) * (rpm / 120.0);
            double v = Vdot_m3_s / Math.Max(1e-6, A_th_m2);

            double K_th = 1.6; // butterfly throttles at WOT are still not lossless
            double dP_Pa = K_th * 0.5 * rho_amb * v * v;
            double dP_kPa = dP_Pa / 1000.0;

            double ratio = 1.0 - Clamp(dP_kPa / Math.Max(1e-6, P_amb_kPa), 0.0, 0.30);
            return Clamp(ratio, 0.70, 1.0);
        }

        /// <summary>
        /// Primary gas velocity (m/s) using mass flow from VE0. Assumes one primary per cylinder.
        /// </summary>
        private static double PrimaryGasVelocity_mps(EngineModel e, int rpm, double ve0, double Vd_total_m3, double rho_amb)
        {
            // approximate exhaust density (hot gas)
            double rho_exh = 0.45; // kg/m^3
            // mass flow (kg/s) using ambient density proxy
            double m_dot_air = (rho_amb * Vd_total_m3 * Clamp(ve0, 0.2, 1.2)) * (rpm / 120.0);

            // geometry
            double D_m = Math.Max(0.010, e.PrimaryID_mm / 1000.0);
            double A_m2 = Math.PI * 0.25 * D_m * D_m;
            int Nprim = Math.Max(1, e.Cylinders);

            return m_dot_air / Math.Max(1e-9, rho_exh * A_m2 * Nprim);
        }

        /// <summary>
        /// Exhaust choke factor on VE from primary gas velocity (strong but bounded).
        /// f ≈ 1 - K * Mach^2 * rpmWeight, clamped to [0.80, 1.0].
        /// </summary>
        private static double ExhaustChokeFactorFromVelocity(EngineModel e, int rpm, double vPrim_mps)
        {
            // speed of sound in hot exhaust ~ 500–560 m/s; pick a mid value
            double a = 520.0;
            double Mach = vPrim_mps / a;
            double rpmWeight = Clamp(rpm / Math.Max(1000.0, (double)e.Redline_RPM), 0.4, 1.2);
            double K = 2.0; // stronger than before so PrimaryID has a visible effect

            double f = 1.0 - K * (Mach * Mach) * rpmWeight;
            return Clamp(f, 0.80, 1.0);
        }

        /// <summary>
        /// Extra backpressure from the exhaust primary (kPa) based on ID, length, rpm (via m_dot).
        /// ΔP ≈ K * 0.5 * ρ * v^2, with K including a length/diameter term and header layout factor.
        /// </summary>
        private static double ExhaustPrimaryFriction_kPa(EngineModel e, int rpm, double m_dot_air)
        {
            double rho_exh = 0.45; // kg/m^3 (hot gas)
            double D_m = Math.Max(0.010, e.PrimaryID_mm / 1000.0);
            double A_m2 = Math.PI * 0.25 * D_m * D_m;
            int Nprim = Math.Max(1, e.Cylinders);

            double v = m_dot_air / Math.Max(1e-9, rho_exh * A_m2 * Nprim); // m/s

            double L_m = Math.Max(0.10, e.PrimaryLength1_mm / 1000.0);
            double K_len = 0.45 * (L_m / Math.Max(0.010, D_m)); // stronger L/D influence
            double K_base = 2.0;

            double layoutG = e.Header switch
            {
                HeaderLayout._421 => 1.15,
                HeaderLayout.UEL => 1.10,
                _ => 1.00
            };

            // slight rpm emphasis
            double rpmG = Clamp(rpm / Math.Max(1000.0, (double)e.Redline_RPM), 0.6, 1.3);

            double K = (K_base + K_len) * layoutG * rpmG;

            double dP_Pa = K * 0.5 * rho_exh * v * v;
            return dP_Pa / 1000.0; // kPa
        }

        private static double VeCeilingFromHeadFlow(EngineModel e, int rpm, double L_intake_m)
        {
            if (!e.Toggles.UseHeadFlowPointsWhenAvailable) return -1;
            if (e.HeadFlow_Intake_CFM28 is null || e.HeadFlow_Intake_CFM28.Count == 0) return -1;

            double cfm = e.HeadFlow_Intake_CFM28.Max(fp => fp.CFM_28); // per valve equiv
            // Convert to m^3/s at 28" H2O: 1 CFM ≈ 0.000471947 m^3/s
            double m3_s = cfm * 0.000471947 * (e.ValvesPerCylinder / 2.0) * e.Cylinders;
            // theoretical volume/s for VE=1 (4-stroke cycles/s = rpm/120)
            double vol_s_at_ve1 = (Math.PI / 4.0) * Math.Pow(e.Bore_mm / 1000.0, 2) *
                                  (e.Stroke_mm / 1000.0) * e.Cylinders * (rpm / 120.0);
            if (vol_s_at_ve1 <= 0) return -1;
            double cap = 0.95 * (m3_s / vol_s_at_ve1); // soft cap
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

        /// <summary>
        /// PMEP baseline + user backpressure (+ primary friction), then reduced by exhaust area at high rpm.
        /// NOTE: counts 100% of extra backpressure now (no attenuation).
        /// </summary>
        private static double PMEP_kPa(EngineModel e, int rpm, double primaryFriction_kPa)
        {
            // Exhaust backpressure above ambient from user inputs
            double extra_kPa = 0.0;
            if (e.ExhaustBackpressure_kPa_ByRPM is { Count: > 0 })
                extra_kPa = LerpMap(e.ExhaustBackpressure_kPa_ByRPM, rpm);
            else if (e.CatBackpressure_kPa is double c)
                extra_kPa = c;

            // Add primary friction
            extra_kPa += Math.Max(0.0, primaryFriction_kPa);

            // intake pumping at WOT (~8 kPa) + full extra backpressure (relieved by area factor)
            double baseIntakePumping_kPa = 8.0;

            // ---- Exhaust-area relief factor (uses exhaust curtain area & rpm) ---
            int valvesExh = Math.Max(1, e.ValvesPerCylinder / 2);
            double de_m = e.ExhaustValveDiameter_mm / 1000.0;
            double le_m = e.Cam.ExhaustMaxLift_mm / 1000.0;
            double curtainExh_m2 = Math.PI * de_m * le_m * valvesExh;
            double boreArea_m2 = Math.PI / 4.0 * Math.Pow(e.Bore_mm / 1000.0, 2);
            double areaExhRatio = curtainExh_m2 / Math.Max(1e-9, boreArea_m2);

            double areaBenefit = Clamp((areaExhRatio - 0.22) / 0.18, 0.0, 1.0);
            double rpmWeight = Clamp(rpm / Math.Max(1.0, (double)e.Redline_RPM), 0.2, 1.0);

            // up to ≈25% reduction of the backpressure-driven term at higher rpm with big exhaust
            double backpressureFactor = 1.0 - 0.25 * areaBenefit * rpmWeight;

            return baseIntakePumping_kPa + Math.Max(0.0, extra_kPa) * backpressureFactor;
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