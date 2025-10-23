namespace EngineLabLib.Models
{
    public sealed record EngineModel
    {
        // ---- Meta / identity ----------------------------------------------------
        public string Name { get; set; } = "Unnamed";
        public Architecture Layout { get; set; } = Architecture.I4;   // Optional

        // ---- Core geometry (Required) ------------------------------------------
        public int Cylinders { get; set; }                             // e.g., 4
        public double Bore_mm { get; set; }                            // e.g., 78.0
        public double Stroke_mm { get; set; }                          // e.g., 83.6
        public double? RodLength_mm { get; set; }                      // Optional; enables better kinematics
                                                                        // Use either CompressionRatio OR full chamber geometry (the solver can derive CR).
        public double? CompressionRatio { get; set; }                  // Required* if you omit the volumes below
        public double? ChamberVolume_cc { get; set; }                  // Optional trio to derive CR
        public double? PistonDishVolume_cc { get; set; }               // Optional
        public double? DeckClearance_mm { get; set; }                  // Optional
        public double? HeadGasketThickness_mm { get; set; }            // Optional
        public double? HeadGasketBore_mm { get; set; }                 // Optional

        // ---- Cylinder head / ports (Required minimal: valve diameters) ---------
        public int ValvesPerCylinder { get; set; }                     // e.g., 4
        public double IntakeValveDiameter_mm { get; set; }             // Required
        public double ExhaustValveDiameter_mm { get; set; }            // Required
        public double? IntakePortDiameter_mm { get; set; }             // Optional (seat/port choke cap)
        public double? ExhaustPortDiameter_mm { get; set; }            // Optional

        // Supply EITHER cam-card basics OR full lift curves (if you have them):
        // ---- Camshafts (Required minimal: lift & duration & LSA) ---------------
        public CamshaftSpec Cam { get; set; } = new();

        // ---- Variable systems (Optional; ignored if null/empty) ----------------
        public VvtSpec? Vvt { get; set; }                              // Intake/exhaust phaser authority + schedule
        public VvlSpec? Vvl { get; set; }                              // Low/High profiles + switch rpm

        // ---- Induction / intake (Required minimal: plenum V + runner L + throttle D) ---
        public double PlenumVolume_cc { get; set; }                    // Required
        public double RunnerLength_mm { get; set; }                    // Required (single-length)
        public double? RunnerLengthShort_mm { get; set; }              // Optional (dual length)
        public int? RunnerSwitchToShort_RPM { get; set; }              // Optional
        public double ThrottleDiameter_mm { get; set; }                // Required
                                                                        // Optional: a measured throttle effective-area map; otherwise use blade-geometry model.
        public SortedDictionary<double, double>? ThrottleAngle_deg_to_EffectiveArea_m2 { get; set; }

        // ---- Exhaust (Required minimal: primary length + ID OR a backpressure curve) ---
        public HeaderLayout Header { get; set; } = HeaderLayout._421;  // Optional
        public double PrimaryLength1_mm { get; set; }                  // Required*
        public double? PrimaryLength2_mm { get; set; }                  // Optional (UEL/421 second pair)
        public double PrimaryID_mm { get; set; }                       // Required*
        public double? CollectorID_mm { get; set; }                    // Optional
        public double? CatBackpressure_kPa { get; set; }               // Optional (constant)
        public SortedDictionary<int, double>? ExhaustBackpressure_kPa_ByRPM { get; set; } // Optional

        // ---- Fuel & mixture (Required minimal: fuel type, stoich AFR, LHV, WOT lambda) --
        public InjectionType Injection { get; set; } = InjectionType.Port;  // Required
        public FuelType Fuel { get; set; } = FuelType.PumpPremium;           // Required
        public double AFR_Stoich { get; set; } = 14.7;                       // Required
        public double Fuel_LHV_MJ_per_kg { get; set; } = 43.0;               // Required
        public double WOT_Lambda { get; set; } = 0.88;                       // Required (target at full load)
                                                                              // Injector/rail (Optional; used to cap power realistically)
        public int InjectorsPerCylinder { get; set; } = 1;
        public double? InjectorFlow_cc_per_min { get; set; }                 // Optional
        public double? FuelPressure_bar { get; set; }                         // Optional
        public double InjectorDutyLimit_0to1 { get; set; } = 0.85;            // Optional

        // ---- Combustion (Wiebe) & knock (Optional; sensible defaults provided) ----------
        public WiebeModel Wiebe { get; set; } = new();                        // Burn law parameters/correlation
        public KnockModel Knock { get; set; } = new();                        // Simple knock margin + caps
                                                                               // Spark strategy: MBT search bound + base table (used as seed); solver finds MBT then caps by knock.
        public SparkStrategy Spark { get; set; } = new();

        // ---- Heat transfer (Woschni-type) (Optional) ---------------------------
        public HeatTransferModel HeatXfer { get; set; } = new();

        // ---- Friction / accessories (Required: FMEP coefficients) --------------
        public FrictionModel Friction { get; set; } = new();                   // A + B*Up + C*Up^2 (kPa)

        // ---- Environment (Required: ambient P,T for air density) ---------------
        public double AmbientPressure_kPa { get; set; } = 101.325;             // Required
        public double AmbientTemp_K { get; set; } = 298.15;                     // Required
        public double IntakeAirTemp_K { get; set; } = 298.15;                   // Required (post-filter, pre-throttle)
        public double ExhaustPressure_kPa { get; set; } = 101.325;              // Optional (downstream reference)

        // ---- Limits / safety (Required) ----------------------------------------
        public int Redline_RPM { get; set; }                                   // Required
        public int RevLimit_RPM { get; set; }                                   // Required
        public int SoftTaper_RPM { get; set; } = 400;                           // Optional: taper window near limiter

        // ---- Optional “data over assumptions” hooks ----------------------------
        public List<FlowPoint28>? HeadFlow_Intake_CFM28 { get; set; }          // Optional: lift→cfm @28" (per-valve equiv)
        public List<FlowPoint28>? HeadFlow_Exhaust_CFM28 { get; set; }         // Optional
        public List<LiftPoint>? IntakeLiftCurve_deg_to_mm { get; set; }        // Optional: full cam lift trace
        public List<LiftPoint>? ExhaustLiftCurve_deg_to_mm { get; set; }       // Optional
        public ResonanceTuning Resonance { get; set; } = new();                 // Optional: tuned-length settings

        // ---- Toggles (Optional) ------------------------------------------------
        public SolverToggles Toggles { get; set; } = new();                     // Enable/disable subsystems cleanly
    }

    // ====== Supporting types ======

    public enum Architecture { I4, V6, V8, Boxer4, Boxer6, I3, I5, Other }
    public enum InjectionType { Carb, TBI, Port, Direct }
    public enum FuelType { PumpRegular, PumpPremium, E85, Race100, Other }
    public enum HeaderLayout { _41, _421, UEL, EL, Stock, Other }
    public enum CompressionBehavior { FixedCR, GeometryDefinesCR }

    public sealed record CamshaftSpec
    {
        // Minimal cam card (solver builds smooth cosine-esque lift from these):
        public double IntakeMaxLift_mm { get; set; }                 // Required
        public double ExhaustMaxLift_mm { get; set; }                // Required
        public double IntakeDuration_deg050 { get; set; }            // Required
        public double ExhaustDuration_deg050 { get; set; }           // Required
        public double LobeSeparationAngle_deg { get; set; }          // Required
                                                                      // Optional installed centerlines (if given, events are exact; else computed from LSA):
        public double? IntakeCenterline_degATDC { get; set; }        // Optional
        public double? ExhaustCenterline_degBTDC { get; set; }       // Optional
    }

    public sealed record CamPhasing
    {
        public double Intake { get; set; }                           // +deg advance
        public double Exhaust { get; set; }                          // -deg retard
    }

    public sealed record VvtSpec
    {
        public double IntakeAdvanceRange_deg { get; set; }           // mech authority
        public double ExhaustRetardRange_deg { get; set; }
        // RPM -> cam phasing (deg). Keys are RPM; values are {intake, exhaust}.
        public SortedDictionary<int, CamPhasing> RpmSchedule { get; set; } = new();
    }

    public sealed record VvlSpec
    {
        public CamshaftSpec LowLift { get; set; } = new();
        public CamshaftSpec HighLift { get; set; } = new();
        public int SwitchOn_RPM { get; set; }
        public int SwitchOff_RPM { get; set; }
    }

    public sealed record WiebeModel
    {
        // Either fixed parameters OR correlation coefficients (solver can compute duration vs. speed/bore).
        public double A { get; set; } = 5.0;                         // shape factor
        public double M { get; set; } = 2.0;                         // form factor
        public double? BurnDuration_deg { get; set; }                // Optional fixed Δθ
                                                                      // Correlation (optional): Δθ = k0 + k1*Bore_mm + k2*Up + ...
        public double? k0 { get; set; }                              // Optional
        public double? k1_Bore { get; set; }                         // Optional
        public double? k2_PistonSpeed { get; set; }                  // Optional
    }

    public sealed record KnockModel
    {
        // Very light-weight knock limiter inputs:
        public double Octane_RON { get; set; } = 98.0;               // fuel knock resistance
        public double SafetyMargin_deg { get; set; } = 2.0;          // degrees shy of knock on MBT cap
        public bool EnableKnockCap { get; set; } = true;
    }

    public sealed record SparkStrategy
    {
        // Solver searches MBT within these bounds each point, then applies knock cap if enabled.
        public double SearchWindowMinus_deg { get; set; } = 10.0;
        public double SearchWindowPlus_deg { get; set; } = 20.0;
        // Optional seed table: RPM → base spark (deg BTDC). Helps the MBT search converge.
        public SortedDictionary<int, double>? BaseSpark_degBTDC_ByRPM { get; set; }
    }

    public sealed record HeatTransferModel
    {
        // Woschni-like constants; keep visible & tweakable.
        public double C1 { get; set; } = 2.28;
        public double C2 { get; set; } = 0.00324;
        public double C3 { get; set; } = 0.0;                         // optional term
                                                                       // Approx wall areas (m²) for head/cylinder/piston if you want better HT; otherwise solver estimates.
        public double? Area_Head_m2 { get; set; }
        public double? Area_Cylinder_m2 { get; set; }
        public double? Area_Piston_m2 { get; set; }
        public double CoolantTemp_K { get; set; } = 363.15;           // ~90°C
    }

    public sealed record FrictionModel
    {
        // FMEP = A + B*Up + C*Up^2  (kPa); Up in m/s
        public double A_kPa { get; set; } = 80.0;                     // Required
        public double B_kPa_per_mps { get; set; } = 6.0;              // Required
        public double C_kPa_per_mps2 { get; set; } = 0.25;            // Required
                                                                       // Optional accessory torque (e.g., alternator) as a function of RPM:
        public SortedDictionary<int, double>? AccessoryTorque_Nm_ByRPM { get; set; }
    }

    public sealed record ResonanceTuning
    {
        // Tuned-length helpers (optional): amplitudes & widths the solver uses to modulate VE via intake/exhaust waves.
        public double IntakeBumpGain_0to1 { get; set; } = 0.03;
        public double ExhaustBumpGain_0to1 { get; set; } = 0.03;
        public double ExhaustDipGain_0to1 { get; set; } = 0.08;       // for UEL destructive interference
        public double FeatureWidth_RPM { get; set; } = 450.0;         // Gaussian sigma in RPM
                                                                       // Speed-of-sound factor if you want temperature sensitivity (else solver uses air a = sqrt(gamma*R*T)):
        public double? HelmholtzTuningCoeff { get; set; }
    }

    public sealed record FlowPoint28 { public double Lift_mm { get; set; } public double CFM_28 { get; set; } }
    public sealed record LiftPoint { public double CrankDeg { get; set; } public double Lift_mm { get; set; } }

    public sealed record SolverToggles
    {
        public bool UseHeadFlowPointsWhenAvailable { get; set; } = true; // else use Cd(λ) correlation
        public bool EnableResonanceModel { get; set; } = true;
        public bool EnableHeatTransfer { get; set; } = true;
        public bool EnableKnockLimit { get; set; } = true;
        public bool EnableInjectorCapacityLimit { get; set; } = true;
        public CompressionBehavior CompressionBehavior { get; set; } = CompressionBehavior.GeometryDefinesCR;          // true: use detailed chamber geometry if available
    }

}