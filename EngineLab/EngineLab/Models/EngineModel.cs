namespace EngineLab.Models
{
    public sealed class EngineModel
    {
        // ---- Meta / identity ----------------------------------------------------
        public string Name { get; init; } = "Unnamed";
        public Architecture Layout { get; init; } = Architecture.I4;   // Optional

        // ---- Core geometry (Required) ------------------------------------------
        public int Cylinders { get; init; }                             // e.g., 4
        public double Bore_mm { get; init; }                            // e.g., 78.0
        public double Stroke_mm { get; init; }                          // e.g., 83.6
        public double? RodLength_mm { get; init; }                      // Optional; enables better kinematics
                                                                        // Use either CompressionRatio OR full chamber geometry (the solver can derive CR).
        public double? CompressionRatio { get; init; }                  // Required* if you omit the volumes below
        public double? ChamberVolume_cc { get; init; }                  // Optional trio to derive CR
        public double? PistonDishVolume_cc { get; init; }               // Optional
        public double? DeckClearance_mm { get; init; }                  // Optional
        public double? HeadGasketThickness_mm { get; init; }            // Optional
        public double? HeadGasketBore_mm { get; init; }                 // Optional

        // ---- Cylinder head / ports (Required minimal: valve diameters) ---------
        public int ValvesPerCylinder { get; init; }                     // e.g., 4
        public double IntakeValveDiameter_mm { get; init; }             // Required
        public double ExhaustValveDiameter_mm { get; init; }            // Required
        public double? IntakePortDiameter_mm { get; init; }             // Optional (seat/port choke cap)
        public double? ExhaustPortDiameter_mm { get; init; }            // Optional

        // Supply EITHER cam-card basics OR full lift curves (if you have them):
        // ---- Camshafts (Required minimal: lift & duration & LSA) ---------------
        public CamshaftSpec Cam { get; init; } = new();

        // ---- Variable systems (Optional; ignored if null/empty) ----------------
        public VvtSpec? Vvt { get; init; }                              // Intake/exhaust phaser authority + schedule
        public VvlSpec? Vvl { get; init; }                              // Low/High profiles + switch rpm

        // ---- Induction / intake (Required minimal: plenum V + runner L + throttle D) ---
        public double PlenumVolume_cc { get; init; }                    // Required
        public double RunnerLength_mm { get; init; }                    // Required (single-length)
        public double? RunnerLengthShort_mm { get; init; }              // Optional (dual length)
        public int? RunnerSwitchToShort_RPM { get; init; }              // Optional
        public double ThrottleDiameter_mm { get; init; }                // Required
                                                                        // Optional: a measured throttle effective-area map; otherwise use blade-geometry model.
        public SortedDictionary<double, double>? ThrottleAngle_deg_to_EffectiveArea_m2 { get; init; }

        // ---- Exhaust (Required minimal: primary length + ID OR a backpressure curve) ---
        public HeaderLayout Header { get; init; } = HeaderLayout._421;  // Optional
        public double PrimaryLength1_mm { get; init; }                  // Required*
        public double PrimaryLength2_mm { get; init; }                  // Optional (UEL/421 second pair)
        public double PrimaryID_mm { get; init; }                       // Required*
        public double? CollectorID_mm { get; init; }                    // Optional
        public double? CatBackpressure_kPa { get; init; }               // Optional (constant)
        public SortedDictionary<int, double>? ExhaustBackpressure_kPa_ByRPM { get; init; } // Optional

        // ---- Fuel & mixture (Required minimal: fuel type, stoich AFR, LHV, WOT lambda) --
        public InjectionType Injection { get; init; } = InjectionType.Port;  // Required
        public FuelType Fuel { get; init; } = FuelType.PumpPremium;           // Required
        public double AFR_Stoich { get; init; } = 14.7;                       // Required
        public double Fuel_LHV_MJ_per_kg { get; init; } = 43.0;               // Required
        public double WOT_Lambda { get; init; } = 0.88;                       // Required (target at full load)
                                                                              // Injector/rail (Optional; used to cap power realistically)
        public int InjectorsPerCylinder { get; init; } = 1;
        public double? InjectorFlow_cc_per_min { get; init; }                 // Optional
        public double? FuelPressure_bar { get; init; }                         // Optional
        public double InjectorDutyLimit_0to1 { get; init; } = 0.85;            // Optional

        // ---- Combustion (Wiebe) & knock (Optional; sensible defaults provided) ----------
        public WiebeModel Wiebe { get; init; } = new();                        // Burn law parameters/correlation
        public KnockModel Knock { get; init; } = new();                        // Simple knock margin + caps
                                                                               // Spark strategy: MBT search bound + base table (used as seed); solver finds MBT then caps by knock.
        public SparkStrategy Spark { get; init; } = new();

        // ---- Heat transfer (Woschni-type) (Optional) ---------------------------
        public HeatTransferModel HeatXfer { get; init; } = new();

        // ---- Friction / accessories (Required: FMEP coefficients) --------------
        public FrictionModel Friction { get; init; } = new();                   // A + B*Up + C*Up^2 (kPa)

        // ---- Environment (Required: ambient P,T for air density) ---------------
        public double AmbientPressure_kPa { get; init; } = 101.325;             // Required
        public double AmbientTemp_K { get; init; } = 298.15;                     // Required
        public double IntakeAirTemp_K { get; init; } = 298.15;                   // Required (post-filter, pre-throttle)
        public double ExhaustPressure_kPa { get; init; } = 101.325;              // Optional (downstream reference)

        // ---- Limits / safety (Required) ----------------------------------------
        public int Redline_RPM { get; init; }                                   // Required
        public int RevLimit_RPM { get; init; }                                   // Required
        public int SoftTaper_RPM { get; init; } = 400;                           // Optional: taper window near limiter

        // ---- Optional “data over assumptions” hooks ----------------------------
        public List<FlowPoint28>? HeadFlow_Intake_CFM28 { get; init; }          // Optional: lift→cfm @28" (per-valve equiv)
        public List<FlowPoint28>? HeadFlow_Exhaust_CFM28 { get; init; }         // Optional
        public List<LiftPoint>? IntakeLiftCurve_deg_to_mm { get; init; }        // Optional: full cam lift trace
        public List<LiftPoint>? ExhaustLiftCurve_deg_to_mm { get; init; }       // Optional
        public ResonanceTuning Resonance { get; init; } = new();                 // Optional: tuned-length settings

        // ---- Toggles (Optional) ------------------------------------------------
        public SolverToggles Toggles { get; init; } = new();                     // Enable/disable subsystems cleanly
    }

    // ====== Supporting types ======

    public enum Architecture { I4, V6, V8, Boxer4, Boxer6, I3, I5, Other }
    public enum InjectionType { Carb, TBI, Port, Direct }
    public enum FuelType { PumpRegular, PumpPremium, E85, Race100, Other }
    public enum HeaderLayout { _41, _421, UEL, EL, Stock, Other }

    public sealed class CamshaftSpec
    {
        // Minimal cam card (solver builds smooth cosine-esque lift from these):
        public double IntakeMaxLift_mm { get; init; }                 // Required
        public double ExhaustMaxLift_mm { get; init; }                // Required
        public double IntakeDuration_deg050 { get; init; }            // Required
        public double ExhaustDuration_deg050 { get; init; }           // Required
        public double LobeSeparationAngle_deg { get; init; }          // Required
                                                                      // Optional installed centerlines (if given, events are exact; else computed from LSA):
        public double? IntakeCenterline_degATDC { get; init; }        // Optional
        public double? ExhaustCenterline_degBTDC { get; init; }       // Optional
    }

    public sealed class CamPhasing
    {
        public double Intake { get; init; }                           // +deg advance
        public double Exhaust { get; init; }                          // -deg retard
    }

    public sealed class VvtSpec
    {
        public double IntakeAdvanceRange_deg { get; init; }           // mech authority
        public double ExhaustRetardRange_deg { get; init; }
        // RPM -> cam phasing (deg). Keys are RPM; values are {intake, exhaust}.
        public SortedDictionary<int, CamPhasing> RpmSchedule { get; init; } = new();
    }

    public sealed class VvlSpec
    {
        public CamshaftSpec LowLift { get; init; } = new();
        public CamshaftSpec HighLift { get; init; } = new();
        public int SwitchOn_RPM { get; init; }
        public int SwitchOff_RPM { get; init; }
    }

    public sealed class WiebeModel
    {
        // Either fixed parameters OR correlation coefficients (solver can compute duration vs. speed/bore).
        public double A { get; init; } = 5.0;                         // shape factor
        public double M { get; init; } = 2.0;                         // form factor
        public double? BurnDuration_deg { get; init; }                // Optional fixed Δθ
                                                                      // Correlation (optional): Δθ = k0 + k1*Bore_mm + k2*Up + ...
        public double? k0 { get; init; }                              // Optional
        public double? k1_Bore { get; init; }                         // Optional
        public double? k2_PistonSpeed { get; init; }                  // Optional
    }

    public sealed class KnockModel
    {
        // Very light-weight knock limiter inputs:
        public double Octane_RON { get; init; } = 98.0;               // fuel knock resistance
        public double SafetyMargin_deg { get; init; } = 2.0;          // degrees shy of knock on MBT cap
        public bool EnableKnockCap { get; init; } = true;
    }

    public sealed class SparkStrategy
    {
        // Solver searches MBT within these bounds each point, then applies knock cap if enabled.
        public double SearchWindowMinus_deg { get; init; } = 10.0;
        public double SearchWindowPlus_deg { get; init; } = 20.0;
        // Optional seed table: RPM → base spark (deg BTDC). Helps the MBT search converge.
        public SortedDictionary<int, double>? BaseSpark_degBTDC_ByRPM { get; init; }
    }

    public sealed class HeatTransferModel
    {
        // Woschni-like constants; keep visible & tweakable.
        public double C1 { get; init; } = 2.28;
        public double C2 { get; init; } = 0.00324;
        public double C3 { get; init; } = 0.0;                         // optional term
                                                                       // Approx wall areas (m²) for head/cylinder/piston if you want better HT; otherwise solver estimates.
        public double? Area_Head_m2 { get; init; }
        public double? Area_Cylinder_m2 { get; init; }
        public double? Area_Piston_m2 { get; init; }
        public double CoolantTemp_K { get; init; } = 363.15;           // ~90°C
    }

    public sealed class FrictionModel
    {
        // FMEP = A + B*Up + C*Up^2  (kPa); Up in m/s
        public double A_kPa { get; init; } = 80.0;                     // Required
        public double B_kPa_per_mps { get; init; } = 6.0;              // Required
        public double C_kPa_per_mps2 { get; init; } = 0.25;            // Required
                                                                       // Optional accessory torque (e.g., alternator) as a function of RPM:
        public SortedDictionary<int, double>? AccessoryTorque_Nm_ByRPM { get; init; }
    }

    public sealed class ResonanceTuning
    {
        // Tuned-length helpers (optional): amplitudes & widths the solver uses to modulate VE via intake/exhaust waves.
        public double IntakeBumpGain_0to1 { get; init; } = 0.03;
        public double ExhaustBumpGain_0to1 { get; init; } = 0.03;
        public double ExhaustDipGain_0to1 { get; init; } = 0.08;       // for UEL destructive interference
        public double FeatureWidth_RPM { get; init; } = 450.0;         // Gaussian sigma in RPM
                                                                       // Speed-of-sound factor if you want temperature sensitivity (else solver uses air a = sqrt(gamma*R*T)):
        public double? HelmholtzTuningCoeff { get; init; }
    }

    public sealed class FlowPoint28 { public double Lift_mm { get; init; } public double CFM_28 { get; init; } }
    public sealed class LiftPoint { public double CrankDeg { get; init; } public double Lift_mm { get; init; } }

    public sealed class SolverToggles
    {
        public bool UseHeadFlowPointsWhenAvailable { get; init; } = true; // else use Cd(λ) correlation
        public bool EnableResonanceModel { get; init; } = true;
        public bool EnableHeatTransfer { get; init; } = true;
        public bool EnableKnockLimit { get; init; } = true;
        public bool EnableInjectorCapacityLimit { get; init; } = true;
    }

}