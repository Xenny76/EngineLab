using EngineLabLib.Models;
using EngineLabLib.Services;
namespace EngineLabTests
{
    public class EngineLabIoTests
    {
        private static EngineModel MinimalValidB6()
        {
            return new EngineModel
            {
                Name = "Miata B6-ZE (minimal valid)",
                Layout = Architecture.I4,

                Cylinders = 4,
                Bore_mm = 78.0,
                Stroke_mm = 83.6,
                CompressionRatio = 9.4,

                ValvesPerCylinder = 4,
                IntakeValveDiameter_mm = 31.0,
                ExhaustValveDiameter_mm = 26.0,

                Cam = new CamshaftSpec
                {
                    IntakeMaxLift_mm = 9.0,
                    ExhaustMaxLift_mm = 9.0,
                    IntakeDuration_deg050 = 230,
                    ExhaustDuration_deg050 = 224,
                    LobeSeparationAngle_deg = 110
                },

                PlenumVolume_cc = 2000,
                RunnerLength_mm = 320,
                ThrottleDiameter_mm = 55,

                Header = HeaderLayout._421,
                PrimaryLength1_mm = 420,
                PrimaryID_mm = 36,

                Injection = InjectionType.Port,
                Fuel = FuelType.PumpRegular,
                AFR_Stoich = 14.7,
                Fuel_LHV_MJ_per_kg = 43.0,
                WOT_Lambda = 0.88,

                Friction = new FrictionModel { A_kPa = 80, B_kPa_per_mps = 6.0, C_kPa_per_mps2 = 0.25 },

                AmbientPressure_kPa = 101.325,
                AmbientTemp_K = 298.15,
                IntakeAirTemp_K = 298.15,

                Redline_RPM = 7200,
                RevLimit_RPM = 7300
            };
        }

        [Fact]
        public void RoundTrip_MinimalSpec_SucceedsAndPreservesCoreFields()
        {
            var spec = MinimalValidB6();
            var json = JsonLoadSave.ToJson(spec);
            var back = JsonLoadSave.FromJson(json);

            Assert.Equal(spec.Name, back.Name);
            Assert.Equal(Architecture.I4, back.Layout);
            Assert.Equal(4, back.Cylinders);
            Assert.Equal(78.0, back.Bore_mm, 3);
            Assert.Equal(83.6, back.Stroke_mm, 3);
            Assert.Equal(9.4, back.CompressionRatio!.Value, 3);
            Assert.Equal(31.0, back.IntakeValveDiameter_mm, 3);
            Assert.Equal(26.0, back.ExhaustValveDiameter_mm, 3);
            Assert.Equal(2000, back.PlenumVolume_cc, 3);
            Assert.Equal(320, back.RunnerLength_mm, 3);
            Assert.Equal(55, back.ThrottleDiameter_mm, 3);
            Assert.Equal(HeaderLayout._421, back.Header);
            Assert.Equal(420, back.PrimaryLength1_mm, 3);
            Assert.Equal(36, back.PrimaryID_mm, 3);
            Assert.Equal(InjectionType.Port, back.Injection);
            Assert.Equal(FuelType.PumpRegular, back.Fuel);
            Assert.Equal(14.7, back.AFR_Stoich, 3);
            Assert.Equal(43.0, back.Fuel_LHV_MJ_per_kg, 3);
            Assert.Equal(0.88, back.WOT_Lambda, 3);
            Assert.Equal(7200, back.Redline_RPM);
            Assert.Equal(7300, back.RevLimit_RPM);
        }

        [Fact]
        public void Save_Then_Load_File_RoundTrips()
        {
            var spec = MinimalValidB6();
            var tmp = Path.Combine(Path.GetTempPath(), $"b6ze_{Guid.NewGuid():N}.json");

            try
            {
                JsonLoadSave.Save(spec, tmp);
                Assert.True(File.Exists(tmp));

                var loaded = JsonLoadSave.Load(tmp);
                Assert.Equal(spec.Cylinders, loaded.Cylinders);
                Assert.Equal(spec.Cam.IntakeDuration_deg050, loaded.Cam.IntakeDuration_deg050);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Fact]
        public void Deserialize_WithCommentsAndTrailingCommas_LenientParsingWorks()
        {
            var json = @"
        {
          // comments are allowed
          ""name"": ""Lenient B6"",
          ""layout"": ""i4"",
          ""cylinders"": ""4"",  // numbers-as-strings ok
          ""bore_mm"": ""78"",
          ""stroke_mm"": 83.6,
          ""compressionRatio"": ""9.4"",
          ""valvesPerCylinder"": 4,
          ""intakeValveDiameter_mm"": 31,
          ""exhaustValveDiameter_mm"": 26,
          ""cam"": {
            ""intakeMaxLift_mm"": ""9.0"",
            ""exhaustMaxLift_mm"": 9.0,
            ""intakeDuration_deg050"": 230,
            ""exhaustDuration_deg050"": 224,
            ""lobeSeparationAngle_deg"": 110,
          },
          ""plenumVolume_cc"": 2000,
          ""runnerLength_mm"": 320,
          ""throttleDiameter_mm"": 55,
          ""header"": ""_421"",
          ""primaryLength1_mm"": 420,
          ""primaryID_mm"": 36,
          ""injection"": ""port"",
          ""fuel"": ""pumpRegular"",
          ""afr_Stoich"": 14.7,
          ""fuel_LHV_MJ_per_kg"": 43.0,
          ""wot_Lambda"": 0.88,
          ""friction"": { ""a_kPa"": 80, ""b_kPa_per_mps"": 6.0, ""c_kPa_per_mps2"": 0.25 },
          ""ambientPressure_kPa"": 101.325,
          ""ambientTemp_K"": 298.15,
          ""intakeAirTemp_K"": 298.15,
          ""redline_RPM"": ""7200"",
          ""revLimit_RPM"": 7300,
        }";

            var spec = JsonLoadSave.FromJson(json);
            Assert.Equal("Lenient B6", spec.Name);
            Assert.Equal(4, spec.Cylinders);
            Assert.Equal(9.4, spec.CompressionRatio!.Value, 3);
            Assert.Equal(110, spec.Cam.LobeSeparationAngle_deg, 3);
        }

        [Fact]
        public void Deserialize_VvtSchedule_IntKeysAreAcceptedAndOrdered()
        {
            var json = @"
        {
          ""name"": ""B6 w/ VVT"",
          ""cylinders"": 4, ""bore_mm"": 78, ""stroke_mm"": 83.6, ""compressionRatio"": 9.4,
          ""valvesPerCylinder"": 4, ""intakeValveDiameter_mm"": 31, ""exhaustValveDiameter_mm"": 26,
          ""cam"": { ""intakeMaxLift_mm"": 9.0, ""exhaustMaxLift_mm"": 9.0, ""intakeDuration_deg050"": 230, ""exhaustDuration_deg050"": 224, ""lobeSeparationAngle_deg"": 110 },
          ""plenumVolume_cc"": 2000, ""runnerLength_mm"": 320, ""throttleDiameter_mm"": 55,
          ""header"": ""_421"", ""primaryLength1_mm"": 420, ""primaryID_mm"": 36,
          ""injection"": ""port"", ""fuel"": ""pumpPremium"", ""afr_Stoich"": 14.7, ""fuel_LHV_MJ_per_kg"": 43.0, ""wot_Lambda"": 0.88,
          ""friction"": { ""a_kPa"": 80, ""b_kPa_per_mps"": 6.0, ""c_kPa_per_mps2"": 0.25 },
          ""ambientPressure_kPa"": 101.325, ""ambientTemp_K"": 298.15, ""intakeAirTemp_K"": 298.15,
          ""redline_RPM"": 7200, ""revLimit_RPM"": 7300,
          ""vvt"": {
            ""intakeAdvanceRange_deg"": 60,
            ""exhaustRetardRange_deg"": 54,
            ""rpmSchedule"": {
              ""1500"": { ""intake"": 8,  ""exhaust"": 4 },
              ""3000"": { ""intake"": 16, ""exhaust"": 10 },
              ""5000"": { ""intake"": 12, ""exhaust"": 8 }
            }
          }
        }";
            var spec = JsonLoadSave.FromJson(json);
            Assert.NotNull(spec.Vvt);
            Assert.True(spec.Vvt!.RpmSchedule.ContainsKey(1500));
            Assert.True(spec.Vvt!.RpmSchedule.ContainsKey(3000));
            Assert.True(spec.Vvt!.RpmSchedule.ContainsKey(5000));
            Assert.Equal(16, spec.Vvt!.RpmSchedule[3000].Intake, 3);
            Assert.Equal(10, spec.Vvt!.RpmSchedule[3000].Exhaust, 3);

            // Ensure sorted order by keys (important for interpolation later)
            var keys = new List<int>(spec.Vvt!.RpmSchedule.Keys);
            Assert.Equal(new[] { 1500, 3000, 5000 }, keys);
        }

        [Fact]
        public void Validation_MissingExhaustAndCurve_Throws()
        {
            var bad = MinimalValidB6();
            bad.PrimaryLength1_mm = 0;   // remove geometry
            bad.PrimaryID_mm = 0;        // remove geometry
            bad.ExhaustBackpressure_kPa_ByRPM = null;

            var ex = Assert.Throws<InvalidDataException>(() => JsonLoadSave.ToJson(bad));
            Assert.Contains("Provide PrimaryLength1_mm & PrimaryID_mm, or an ExhaustBackpressure_kPa_ByRPM curve", ex.Message);
        }

        [Fact]
        public void Validation_WithBackpressureCurve_NoExhaustGeometry_IsOk()
        {
            var spec = MinimalValidB6();
            spec.PrimaryLength1_mm = 0;    // intentionally omit geometry
            spec.PrimaryID_mm = 0;
            spec.ExhaustBackpressure_kPa_ByRPM = new SortedDictionary<int, double>
            {
                [2000] = 2.0,
                [4000] = 3.0,
                [6000] = 3.5
            };

            var json = JsonLoadSave.ToJson(spec);
            var back = JsonLoadSave.FromJson(json);

            Assert.NotNull(back.ExhaustBackpressure_kPa_ByRPM);
            Assert.Equal(3, back.ExhaustBackpressure_kPa_ByRPM!.Count);
            Assert.False(back.PrimaryID_mm > 0);
        }

        [Fact]
        public void Serializer_WritesEnumsAsStrings()
        {
            var spec = MinimalValidB6();
            var json = JsonLoadSave.ToJson(spec);

            Assert.Contains(@"""layout"": ""i4""", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(@"""injection"": ""port""", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(@"""fuel"": ""pumpRegular""", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(@"""header"": ""_421""", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Defaults_AreStableOnRoundTrip()
        {
            var spec = MinimalValidB6();

            // Leave optional nested models at defaults
            var json = JsonLoadSave.ToJson(spec);
            var back = JsonLoadSave.FromJson(json);

            // Check a few defaulted subfields survived round-trip
            Assert.Equal(5.0, back.Wiebe.A, 3);
            Assert.Equal(2.0, back.Wiebe.M, 3);
            Assert.True(back.Toggles.EnableResonanceModel);
            Assert.True(back.Toggles.EnableKnockLimit);
        }

        [Fact]
        public void MissingCoreGeometry_Throws()
        {
            var e = new EngineModel
            {
                Name = "Bad",
                Cylinders = 0, // bad
                Bore_mm = 78,
                Stroke_mm = 83.6,
                CompressionRatio = 9.4,
                ValvesPerCylinder = 4,
                IntakeValveDiameter_mm = 31,
                ExhaustValveDiameter_mm = 26,
                PlenumVolume_cc = 2000,
                RunnerLength_mm = 320,
                ThrottleDiameter_mm = 55,
                PrimaryLength1_mm = 420,
                PrimaryID_mm = 36,
                Injection = InjectionType.Port,
                Fuel = FuelType.PumpPremium,
                AFR_Stoich = 14.7,
                Fuel_LHV_MJ_per_kg = 43.0,
                WOT_Lambda = 0.88,
                Friction = new FrictionModel { A_kPa = 80, B_kPa_per_mps = 6.0, C_kPa_per_mps2 = 0.25 },
                AmbientPressure_kPa = 101.325,
                AmbientTemp_K = 298.15,
                IntakeAirTemp_K = 298.15,
                Redline_RPM = 7200,
                RevLimit_RPM = 7300
            };

            var ex = Assert.Throws<InvalidDataException>(() => JsonLoadSave.ToJson(e));
            Assert.Contains("Cylinders must be > 0", ex.Message);
        }

        [Fact]
        public void MissingCompressionAndVolumes_Throws()
        {
            var e = new EngineModel
            {
                Name = "BadCR",
                Cylinders = 4,
                Bore_mm = 78,
                Stroke_mm = 83.6,
                // CompressionRatio = null and not enough volume fields -> invalid
                ValvesPerCylinder = 4,
                IntakeValveDiameter_mm = 31,
                ExhaustValveDiameter_mm = 26,
                Cam = new CamshaftSpec { IntakeMaxLift_mm = 9, ExhaustMaxLift_mm = 9, IntakeDuration_deg050 = 230, ExhaustDuration_deg050 = 224, LobeSeparationAngle_deg = 110 },
                PlenumVolume_cc = 2000,
                RunnerLength_mm = 320,
                ThrottleDiameter_mm = 55,
                PrimaryLength1_mm = 420,
                PrimaryID_mm = 36,
                Injection = InjectionType.Port,
                Fuel = FuelType.PumpPremium,
                AFR_Stoich = 14.7,
                Fuel_LHV_MJ_per_kg = 43.0,
                WOT_Lambda = 0.88,
                Friction = new FrictionModel { A_kPa = 80, B_kPa_per_mps = 6.0, C_kPa_per_mps2 = 0.25 },
                AmbientPressure_kPa = 101.325,
                AmbientTemp_K = 298.15,
                IntakeAirTemp_K = 298.15,
                Redline_RPM = 7200,
                RevLimit_RPM = 7300
            };

            var ex = Assert.Throws<InvalidDataException>(() => JsonLoadSave.ToJson(e));
            Assert.Contains("Provide CompressionRatio OR the volumes to derive it", ex.Message);
        }

        [Fact]
        public void WotLambda_OutOfRange_Throws()
        {
            var e = new EngineModel
            {
                Name = "LambdaBad",
                Cylinders = 4,
                Bore_mm = 78,
                Stroke_mm = 83.6,
                CompressionRatio = 9.4,
                ValvesPerCylinder = 4,
                IntakeValveDiameter_mm = 31,
                ExhaustValveDiameter_mm = 26,
                PlenumVolume_cc = 2000,
                RunnerLength_mm = 320,
                ThrottleDiameter_mm = 55,
                PrimaryLength1_mm = 420,
                PrimaryID_mm = 36,
                Injection = InjectionType.Port,
                Fuel = FuelType.PumpPremium,
                AFR_Stoich = 14.7,
                Fuel_LHV_MJ_per_kg = 43.0,
                WOT_Lambda = 0.5, // too rich
                Friction = new FrictionModel { A_kPa = 80, B_kPa_per_mps = 6.0, C_kPa_per_mps2 = 0.25 },
                AmbientPressure_kPa = 101.325,
                AmbientTemp_K = 298.15,
                IntakeAirTemp_K = 298.15,
                Redline_RPM = 7200,
                RevLimit_RPM = 7300
            };

            var ex = Assert.Throws<InvalidDataException>(() => JsonLoadSave.ToJson(e));
            Assert.Contains("WOT_Lambda out of reasonable range", ex.Message);
        }
    }
}
