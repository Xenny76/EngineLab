using EngineLab.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace EngineLab.Services
{
    public static class JsonLoadSave
    {
        // ===== JSON options shared by save/load =====
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip, // allow // and /* */ in files
            AllowTrailingCommas = true,
            Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), // enums as strings
            new JsonDoubleLenientConverter(),                        // 1, "1", 1.0 all ok
            new SortedDictionaryIntKeyConverterFactory()             // robust int-key maps
        }
        };

        // ===== Public API =====
        public static string ToJson(EngineModel spec)
        {
            ValidateOrThrow(spec);
            return JsonSerializer.Serialize(spec, Options);
        }

        public static void Save(EngineModel spec, string path)
        {
            var json = ToJson(spec);
            File.WriteAllText(path, json);
        }

        public static EngineModel FromJson(string json)
        {
            var spec = JsonSerializer.Deserialize<EngineModel>(json, Options)
                       ?? throw new InvalidDataException("EngineModel JSON parsed null.");
            ValidateOrThrow(spec);
            return spec;
        }

        public static EngineModel Load(string path)
        {
            var json = File.ReadAllText(path);
            return FromJson(json);
        }

        // ===== Lightweight validation =====
        private static void ValidateOrThrow(EngineModel e)
        {
            var errs = new List<string>();

            if (e.Cylinders <= 0) errs.Add("Cylinders must be > 0.");
            if (e.Bore_mm <= 0 || e.Stroke_mm <= 0) errs.Add("Bore/Stroke must be > 0.");
            if (e.CompressionRatio is null &&
                (e.ChamberVolume_cc is null || e.HeadGasketThickness_mm is null))
                errs.Add("Provide CompressionRatio OR the volumes to derive it.");

            if (e.ValvesPerCylinder <= 0) errs.Add("ValvesPerCylinder must be > 0.");
            if (e.IntakeValveDiameter_mm <= 0 || e.ExhaustValveDiameter_mm <= 0)
                errs.Add("Valve diameters must be > 0.");

            if (e.PlenumVolume_cc <= 0) errs.Add("PlenumVolume_cc must be > 0.");
            if (e.RunnerLength_mm <= 0) errs.Add("RunnerLength_mm must be > 0.");
            if (e.ThrottleDiameter_mm <= 0) errs.Add("ThrottleDiameter_mm must be > 0.");

            // exhaust (either geometry OR backpressure curve)
            bool hasExhGeom = e.PrimaryLength1_mm > 0 && e.PrimaryID_mm > 0;
            bool hasExhCurve = e.ExhaustBackpressure_kPa_ByRPM is not null && e.ExhaustBackpressure_kPa_ByRPM.Count > 0;
            if (!hasExhGeom && !hasExhCurve)
                errs.Add("Provide PrimaryLength1_mm & PrimaryID_mm, or an ExhaustBackpressure_kPa_ByRPM curve.");

            if (e.AFR_Stoich <= 0 || e.Fuel_LHV_MJ_per_kg <= 0) errs.Add("Fuel LHV/stoich must be set.");
            if (e.WOT_Lambda <= 0.7 || e.WOT_Lambda > 1.2) errs.Add("WOT_Lambda out of reasonable range (0.7–1.2).");

            if (e.Redline_RPM <= 0 || e.RevLimit_RPM <= 0 || e.RevLimit_RPM < e.Redline_RPM)
                errs.Add("Redline/RevLimit invalid.");

            if (errs.Count > 0) throw new InvalidDataException(string.Join("\n", errs));
        }

        // ===== Converters =====

        // Accept numbers as 1, 1.0, or "1.0"
        private sealed class JsonDoubleLenientConverter : JsonConverter<double>
        {
            public override double Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
            {
                return r.TokenType switch
                {
                    JsonTokenType.Number => r.GetDouble(),
                    JsonTokenType.String => double.TryParse(r.GetString(), out var v) ? v :
                                            throw new JsonException("Invalid double string."),
                    _ => throw new JsonException("Invalid token for double.")
                };
            }
            public override void Write(Utf8JsonWriter w, double v, JsonSerializerOptions o) => w.WriteNumberValue(v);
        }

        // SortedDictionary<int, T> with string/number JSON keys
        private sealed class SortedDictionaryIntKeyConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert)
            {
                if (!typeToConvert.IsGenericType) return false;
                var gen = typeToConvert.GetGenericTypeDefinition();
                return gen == typeof(SortedDictionary<,>) && typeToConvert.GetGenericArguments()[0] == typeof(int);
            }

            public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
            {
                var valueType = type.GetGenericArguments()[1];
                var convType = typeof(SortedDictionaryIntKeyConverter<>).MakeGenericType(valueType);
                return (JsonConverter)Activator.CreateInstance(convType)!;
            }

            private sealed class SortedDictionaryIntKeyConverter<TVal> : JsonConverter<SortedDictionary<int, TVal>>
            {
                public override SortedDictionary<int, TVal> Read(ref Utf8JsonReader r, Type type, JsonSerializerOptions o)
                {
                    if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected object for map.");
                    var dict = new SortedDictionary<int, TVal>();
                    while (r.Read())
                    {
                        if (r.TokenType == JsonTokenType.EndObject) break;
                        if (r.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected property name.");
                        var keyStr = r.GetString()!;
                        if (!int.TryParse(keyStr, out var key)) throw new JsonException($"Key '{keyStr}' not an int.");
                        r.Read();
                        var val = JsonSerializer.Deserialize<TVal>(ref r, o)!;
                        dict[key] = val;
                    }
                    return dict;
                }

                public override void Write(Utf8JsonWriter w, SortedDictionary<int, TVal> value, JsonSerializerOptions o)
                {
                    w.WriteStartObject();
                    foreach (var kv in value)
                    {
                        w.WritePropertyName(kv.Key.ToString());
                        JsonSerializer.Serialize(w, kv.Value, o);
                    }
                    w.WriteEndObject();
                }
            }
        }
    }

}
