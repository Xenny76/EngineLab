using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using EngineLab.Helpers;
using EngineLabLib.Models;
using EngineLabLib.Services;
using EngineLabLib.Simulation;
using EngineLabLib.Session;

namespace EngineLab.Views
{
    public partial class ComparePresetsPage : ContentPage
    {
        private readonly PresetService _presets;

        // Same simulation model pattern as DynoPage
        private readonly SimulationModel _runner = new() { DrivetrainLossFraction = 0.15 };

        public ObservableCollection<EnginePreset> Presets { get; } = new();

        private EnginePreset? _leftPreset;
        private EnginePreset? _rightPreset;

        private bool _rendering;

        public ComparePresetsPage(PresetService presets)
        {
            InitializeComponent();

            _presets = presets;
            BindingContext = this;

            PlotHost.SizeChanged += OnPlotHostSizeChanged;
        }

        private void OnPlotHostSizeChanged(object? sender, EventArgs e)
        {
            _ = RenderPlotAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                StatusLabel.Text = "Loading presets…";

                await _presets.InitializeAsync();

                Presets.Clear();
                foreach (var p in _presets.Presets)
                    Presets.Add(p);

                StatusLabel.Text = Presets.Count == 0
                    ? "No presets found."
                    : "Select one or two presets to compare.";

                await RenderPlotAsync();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Failed to load presets: " + ex.Message;
            }
        }

        // ----------------- selection handlers -----------------

        private async void OnLeftSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _leftPreset = e.CurrentSelection.FirstOrDefault() as EnginePreset;
            await RenderPlotAsync();
        }

        private async void OnRightSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _rightPreset = e.CurrentSelection.FirstOrDefault() as EnginePreset;
            await RenderPlotAsync();
        }

        // ----------------- plotting core ----------------------

        private async Task RenderPlotAsync()
        {
            if (_rendering)
                return;

            _rendering = true;
            try
            {
                if (PlotHost.Width <= 0 || PlotHost.Height <= 0)
                    return;

                DynoCurve? curveA = null;
                DynoCurve? curveB = null;

                if (_leftPreset is not null)
                    curveA = await RunDynoForPresetAsync(_leftPreset);

                if (_rightPreset is not null)
                    curveB = await RunDynoForPresetAsync(_rightPreset);

                var plt = new ScottPlot.Plot();

                bool anyCurves = false;

                if (curveA is not null && !curveA.IsEmpty)
                {
                    AddEngineCurves(plt, curveA, _leftPreset!.Name, isPrimary: true);
                    anyCurves = true;
                }

                if (curveB is not null && !curveB.IsEmpty)
                {
                    AddEngineCurves(plt, curveB, _rightPreset!.Name, isPrimary: false);
                    anyCurves = true;
                }

                // Title
                string title;
                if (_leftPreset is not null && _rightPreset is not null)
                    title = $"{_leftPreset.Name} vs {_rightPreset.Name}";
                else if (_leftPreset is not null)
                    title = _leftPreset.Name;
                else if (_rightPreset is not null)
                    title = _rightPreset.Name;
                else
                    title = "Preset comparison";

                plt.Title(title);
                plt.Axes.Bottom.Label.Text = "RPM";
                plt.Axes.Left.Label.Text = "Horsepower";
                plt.Axes.Right.Label.Text = "Torque (lb-ft)";

                if (anyCurves)
                {
                    // union of ranges across whichever curves we have
                    var allRpm = new List<double>();
                    var allHp = new List<double>();
                    var allTq = new List<double>();

                    void Accumulate(DynoCurve c)
                    {
                        allRpm.AddRange(c.Points.Select(p => (double)p.Rpm));
                        allHp.AddRange(c.Points.Select(p => p.Hp));
                        allTq.AddRange(c.Points.Select(p => p.TorqueLbFt));
                    }

                    if (curveA is not null && !curveA.IsEmpty) Accumulate(curveA);
                    if (curveB is not null && !curveB.IsEmpty) Accumulate(curveB);

                    if (allRpm.Count > 0)
                    {
                        double xMin = allRpm.Min();
                        double xMax = allRpm.Max();
                        double hpMax = allHp.DefaultIfEmpty(0).Max();
                        double tqMax = allTq.DefaultIfEmpty(0).Max();

                        plt.Axes.SetLimits(xMin, xMax, 0, Math.Ceiling(hpMax * 1.10));
                        plt.Axes.SetLimitsY(0, Math.Ceiling(tqMax * 1.10), plt.Axes.Right);
                        plt.Axes.Margins(0, 0);
                    }

                    plt.Legend.IsVisible = true;

                    // Status text
                    if (_leftPreset is not null && _rightPreset is not null)
                        StatusLabel.Text = $"Comparing '{_leftPreset.Name}' vs '{_rightPreset.Name}'.";
                    else if (_leftPreset is not null)
                        StatusLabel.Text = $"Showing '{_leftPreset.Name}' only.";
                    else if (_rightPreset is not null)
                        StatusLabel.Text = $"Showing '{_rightPreset.Name}' only.";
                }
                else
                {
                    plt.Legend.IsVisible = false;
                    StatusLabel.Text = Presets.Count == 0
                        ? "No presets loaded."
                        : "No presets selected. Select one or two presets to compare.";
                }

                double density = DeviceDisplay.MainDisplayInfo.Density;
                int pxW = Math.Max(1, (int)(PlotHost.Width * density));
                int pxH = Math.Max(1, (int)(PlotHost.Height * density));

                var bytes = plt.GetImageBytes(pxW, pxH, ScottPlot.ImageFormat.Png);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DynoImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                });
            }
            finally
            {
                _rendering = false;
            }
        }

        private static void AddEngineCurves(ScottPlot.Plot plt, DynoCurve curve, string label, bool isPrimary)
        {
            var xs = curve.Points.Select(p => (double)p.Rpm).ToArray();
            var hp = curve.Points.Select(p => p.Hp).ToArray();
            var tq = curve.Points.Select(p => p.TorqueLbFt).ToArray();

            // HP on left axis
            var hpLine = plt.Add.Scatter(xs, hp);
            hpLine.LegendText = $"{label} HP";
            hpLine.Axes.YAxis = plt.Axes.Left;
            hpLine.MarkerSize = 0;
            hpLine.LineWidth = isPrimary ? 3 : 2;

            // Torque on right axis (dashed)
            var tqLine = plt.Add.Scatter(xs, tq);
            tqLine.LegendText = $"{label} TQ";
            tqLine.Axes.YAxis = plt.Axes.Right;
            tqLine.MarkerSize = 0;
            tqLine.LineWidth = isPrimary ? 2 : 1;
            tqLine.LinePattern = ScottPlot.LinePattern.Dashed;
        }

        // ----------------- dyno for a single preset -----------

        private async Task<DynoCurve?> RunDynoForPresetAsync(EnginePreset preset)
        {
            try
            {
                var spec = await LoadEngineModelAsync(preset);

                var cfg = new DynoConfig
                {
                    RpmStart = 1500,
                    StepRpm = 100,
                    WheelBasis = true,
                    RevHeadroomRpm = 200,
                    RpmStop = spec.Redline_RPM + 200
                };

                var session = new ModSession(spec, SynchronizationContext.Current);
                var res = session.GetComparison(cfg, _runner);

                // We just need "the curve for this spec" – baseline/current will be identical
                return res.Current;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Dyno failed for '{preset.Name}': {ex.Message}";
                return null;
            }
        }

        // ----------------- JSON loading (same as DynoPage) ----

        private static async Task<EngineModel> LoadEngineModelAsync(EnginePreset preset)
        {
            string json;

            if (preset.IsBuiltIn)
            {
                json = TryLoadEmbedded(preset.JsonFileName)
                       ?? await TryLoadMauiAssetAsync(preset.JsonFileName)
                       ?? throw new FileNotFoundException(
                           $"{preset.JsonFileName} not found as EmbeddedResource or MAUI asset.");
            }
            else
            {
                string folder = Path.Combine(FileSystem.AppDataDirectory, "Presets");
                string path = Path.Combine(folder, preset.JsonFileName);

                if (!File.Exists(path))
                    throw new FileNotFoundException($"User preset JSON not found: {path}");

                json = await File.ReadAllTextAsync(path);
            }

            json = NormalizeForFixedCR(json);
            var spec = JsonLoadSave.FromJson(json); // same call style as DynoPage
            return spec;
        }

        private static string? TryLoadEmbedded(string fileName)
        {
            try { return Embedded.LoadText(fileName); }
            catch { return null; }
        }

        private static async Task<string?> TryLoadMauiAssetAsync(string fileName)
        {
            try
            {
                using var s = await FileSystem.OpenAppPackageFileAsync(fileName);
                using var r = new StreamReader(s);
                return await r.ReadToEndAsync();
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeForFixedCR(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            var toggles = (root["toggles"] as JsonObject) ?? new JsonObject();
            toggles["compressionBehavior"] = "fixedCR";
            root["toggles"] = toggles;
            return root.ToJsonString();
        }
    }
}