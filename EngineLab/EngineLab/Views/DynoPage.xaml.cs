using System.Globalization;
using System.Text.Json.Nodes;

using EngineLab.Helpers;
using EngineLabLib.Models;
using EngineLabLib.Services;
using EngineLabLib.Simulation;
using EngineLabLib.Modification;
using EngineLabLib.Session;

namespace EngineLab.Views
{
    public partial class DynoPage : ContentPage
    {
        // session + dyno
        private ModSession? _session;
        private DynoConfig _cfg = new();
        private readonly SimulationModel _runner = new() { DrivetrainLossFraction = 0.15 };

        private bool _handlingSpecChange;

        // cached series...
        private double[]? _xsBase, _xsCur;
        private double[]? _hpBase, _tqBase;
        private double[]? _hpCur, _tqCur;
        private string _specName = "";

        // remember which preset we last loaded into this session
        private string? _currentPresetId;

        // UI guard etc...
        private bool _updatingUI;
        private readonly Dictionary<string, (Slider slider, Entry entry)> _ctrl = new();

        // presets
        private readonly PresetService _presets;

        public DynoPage(PresetService presets)
        {
            InitializeComponent();

            _presets = presets;

            PlotHost.SizeChanged += (_, __) => RenderPlotToFit();

            _ctrl["Bore_mm"] = (SliderBore, EntryBore);
            _ctrl["Stroke_mm"] = (SliderStroke, EntryStroke);
            _ctrl["WOT_Lambda"] = (SliderLambda, EntryLambda);
            _ctrl["Redline_RPM"] = (SliderRedline, EntryRedline);
            _ctrl["RunnerLength_mm"] = (SliderRunnerLen, EntryRunnerLen);
            _ctrl["ThrottleDiameter_mm"] = (SliderThrottle, EntryThrottle);
            _ctrl["Cam.IntakeDuration_deg050"] = (SliderIntakeDur, EntryIntakeDur);
            _ctrl["Cam.ExhaustDuration_deg050"] = (SliderExhaustDur, EntryExhaustDur);
            _ctrl["Cam.IntakeMaxLift_mm"] = (SliderIntLift, EntryIntLift);
            _ctrl["Cam.ExhaustMaxLift_mm"] = (SliderExhLift, EntryExhLift);
            _ctrl["Cam.LobeSeparationAngle_deg"] = (SliderLSA, EntryLSA);
            _ctrl["PrimaryLength1_mm"] = (SliderPrimLen1, EntryPrimLen1);
            _ctrl["PrimaryLength2_mm"] = (SliderPrimLen2, EntryPrimLen2);
            _ctrl["PrimaryID_mm"] = (SliderPrimID, EntryPrimID);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                await PlotOnceAsync();
            }
            catch (Exception ex)
            {
                Status.Text = "Dyno hard-failed";
                GuardRail.Text = ex.ToString();
                DynoImage.Source = null;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            ResetSession();
        }

        private void ResetSession()
        {
            if (_session is null)
                return;

            _session.OnSpecChanged -= OnSessionSpecChanged;
            _session.OnGuardRail -= OnGuardRailMessage;

            _session = null;
            _currentPresetId = null;
        }

        private void OnGuardRailMessage(string path, string msg)
        {
            MainThread.BeginInvokeOnMainThread(() => GuardRail.Text = $"{path}: {msg}");
        }

        // -------------------- Initial / reload dyno --------------------

        private async Task SafePlotOnceAsync()
        {
            try
            {
                await PlotOnceAsync();
            }
            catch (Exception ex)
            {
                // Catch absolutely anything that leaked out of PlotOnceAsync
                Status.Text = "Dyno hard-failed";
                GuardRail.Text = ex.ToString();
                DynoImage.Source = null;
            }
        }

        private async Task PlotOnceAsync()
        {
            try
            {
                Status.Text = "Checking preset selection…";

                var preset = _presets.CurrentPreset;
                if (preset is null)
                {
                    ClearDynoUi("No preset selected. Choose one on the Presets tab.");
                    return;
                }

                // If we already loaded this exact preset and still have a session, don't rebuild it
                if (_session is not null && _currentPresetId == preset.Id)
                {
                    Status.Text = $"OK — {_specName}";
                    return;
                }

                // Switching preset (or first load) => tear down any old session
                ResetSession();

                // ---------- Load JSON ----------
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

                var spec = JsonLoadSave.FromJson(json);
                _specName = preset.Name ?? spec.Name ?? "";
                Status.Text = $"Loaded preset: {_specName}";

                // ---------- New session ----------
                _session = new ModSession(spec, SynchronizationContext.Current);
                _session.OnSpecChanged += OnSessionSpecChanged;
                _session.OnGuardRail += OnGuardRailMessage;

                _currentPresetId = preset.Id;

                _cfg = new DynoConfig
                {
                    RpmStart = 1500,
                    StepRpm = 100,
                    WheelBasis = true,
                    RevHeadroomRpm = 200,
                    RpmStop = spec.Redline_RPM + 200
                };

                var res0 = _session.GetComparison(_cfg, _runner);
                CacheSeries(res0);

                _updatingUI = true;
                try
                {
                    SetupAllControls(spec);
                    MirrorAll(spec);

                    // enable controls now that we have a valid session/spec
                    foreach (var (_, c) in _ctrl)
                    {
                        c.slider.IsEnabled = true;
                        c.entry.IsEnabled = true;
                    }
                }
                finally
                {
                    _updatingUI = false;
                }

                UpdateMetrics(res0);
                RenderPlotToFit();
                Status.Text = $"OK — {_specName}";
            }
            catch (Exception ex)
            {
                Status.Text = "Dyno failed";
                GuardRail.Text = ex.ToString();
                DynoImage.Source = null;
            }
        }

        private void ClearDynoUi(string statusMessage)
        {
            _session = null;
            _cfg = new DynoConfig();

            _xsBase = _hpBase = _tqBase = null;
            _xsCur = _hpCur = _tqCur = null;
            _currentPresetId = null;

            // Clear slider/entry values and disable them
            _updatingUI = true;
            try
            {
                foreach (var (_, c) in _ctrl)
                {
                    c.slider.Value = c.slider.Minimum; // or any neutral value
                    c.entry.Text = string.Empty;
                    c.slider.IsEnabled = false;
                    c.entry.IsEnabled = false;
                }
            }
            finally
            {
                _updatingUI = false;
            }

            Status.Text = statusMessage;
            Metrics.Text = string.Empty;
            GuardRail.Text = string.Empty;
            DynoImage.Source = null;
        }

        // -------------------- Session event --------------------

        private void OnSessionSpecChanged(EngineModel cur)
        {
            // Prevent re-entrancy (just in case ModSession fires events in a chain)
            if (_handlingSpecChange)
                return;

            _handlingSpecChange = true;
            try
            {
                if (_session is null)
                {
                    ClearDynoUi("No active session during spec change.");
                    return;
                }

                // Grow X-window if redline grows (derived limit = redline + headroom)
                int neededStop = Math.Max(_session.Baseline.Redline_RPM, cur.Redline_RPM) + _cfg.RevHeadroomRpm;
                if (neededStop != _cfg.RpmStop)
                {
                    _cfg = new DynoConfig
                    {
                        RpmStart = _cfg.RpmStart,
                        RpmStop = neededStop,
                        StepRpm = _cfg.StepRpm,
                        WheelBasis = _cfg.WheelBasis,
                        RevHeadroomRpm = _cfg.RevHeadroomRpm
                    };
                }

                // Dyno recompute
                var res = _session.GetComparison(_cfg, _runner);
                CacheSeries(res);

                // Mirror back to controls without firing slider events
                _updatingUI = true;
                try
                {
                    MirrorAll(cur);
                    UpdateConditionalEnable(cur);
                }
                finally
                {
                    _updatingUI = false;
                }

                UpdateMetrics(res);
                RenderPlotToFit();
            }
            catch (Exception ex)
            {
                // Catch any dyno / session weirdness instead of crashing the app
                GuardRail.Text = "Dyno update failed: " + ex.Message;
            }
            finally
            {
                _handlingSpecChange = false;
            }
        }

        // -------------------- Controls setup -------------------

        private void SetupAllControls(EngineModel cur)
        {
            SetupControl("Bore_mm", cur.Bore_mm);
            SetupControl("Stroke_mm", cur.Stroke_mm);
            SetupControl("WOT_Lambda", cur.WOT_Lambda);
            SetupControl("Redline_RPM", cur.Redline_RPM);
            SetupControl("RunnerLength_mm", cur.RunnerLength_mm);
            SetupControl("ThrottleDiameter_mm", cur.ThrottleDiameter_mm);
            SetupControl("Cam.IntakeDuration_deg050", cur.Cam.IntakeDuration_deg050);
            SetupControl("Cam.ExhaustDuration_deg050", cur.Cam.ExhaustDuration_deg050);
            SetupControl("Cam.IntakeMaxLift_mm", cur.Cam.IntakeMaxLift_mm);
            SetupControl("Cam.ExhaustMaxLift_mm", cur.Cam.ExhaustMaxLift_mm);
            SetupControl("Cam.LobeSeparationAngle_deg", cur.Cam.LobeSeparationAngle_deg);
            SetupControl("PrimaryLength1_mm", cur.PrimaryLength1_mm);
            SetupControl("PrimaryLength2_mm", cur.PrimaryLength2_mm ?? cur.PrimaryLength1_mm);
            SetupControl("PrimaryID_mm", cur.PrimaryID_mm);

            UpdateConditionalEnable(cur);
        }

        private void UpdateConditionalEnable(EngineModel cur)
        {
            var eff = PathConstraints.Effective("PrimaryLength2_mm", cur);
            bool enabled = eff.IsEnabledWhen is null || eff.IsEnabledWhen(cur);

            SliderPrimLen2.IsEnabled = enabled;
            EntryPrimLen2.IsEnabled = enabled;
            LblPrimLen2.Opacity = enabled ? 1.0 : 0.5;
        }

        private void SetupControl(string path, double value)
        {
            if (!_ctrl.TryGetValue(path, out var c)) return;

            var eff = PathConstraints.Effective(path, _session?.Current ?? new EngineModel());

            double min = eff.Min ?? (value > 0 ? 0 : value - 10);
            double max = eff.Max ?? (value > 0 ? value * 2 : 100);

            c.slider.Minimum = min;
            c.slider.Maximum = Math.Max(min + (eff.Step > 0 ? eff.Step : 1), max);
            c.slider.Value = Clamp(value, c.slider.Minimum, c.slider.Maximum);

            c.entry.Text = FormatForStep(value, eff.Step);
        }

        private void MirrorAll(EngineModel cur)
        {
            Mirror("Bore_mm", cur.Bore_mm);
            Mirror("Stroke_mm", cur.Stroke_mm);
            Mirror("WOT_Lambda", cur.WOT_Lambda);
            Mirror("Redline_RPM", cur.Redline_RPM);
            Mirror("RunnerLength_mm", cur.RunnerLength_mm);
            Mirror("ThrottleDiameter_mm", cur.ThrottleDiameter_mm);
            Mirror("Cam.IntakeDuration_deg050", cur.Cam.IntakeDuration_deg050);
            Mirror("Cam.ExhaustDuration_deg050", cur.Cam.ExhaustDuration_deg050);
            Mirror("Cam.IntakeMaxLift_mm", cur.Cam.IntakeMaxLift_mm);
            Mirror("Cam.ExhaustMaxLift_mm", cur.Cam.ExhaustMaxLift_mm);
            Mirror("Cam.LobeSeparationAngle_deg", cur.Cam.LobeSeparationAngle_deg);
            Mirror("PrimaryLength1_mm", cur.PrimaryLength1_mm);
            if (cur.PrimaryLength2_mm is double L2)
                Mirror("PrimaryLength2_mm", L2);
            Mirror("PrimaryID_mm", cur.PrimaryID_mm);
        }

        private void Mirror(string path, double value)
        {
            if (!_ctrl.TryGetValue(path, out var c)) return;
            var eff = PathConstraints.Effective(path, _session?.Current ?? new EngineModel());

            double newVal = Clamp(value, c.slider.Minimum, c.slider.Maximum);
            if (Math.Abs(newVal - c.slider.Value) > 1e-6)
                c.slider.Value = newVal;

            c.entry.Text = FormatForStep(value, eff.Step);
        }

        // -------------------- Slider / Entry events ------------

        private void OnSliderChanged(object sender, ValueChangedEventArgs e)
        {
            if (_updatingUI)
                return;
            if (sender is not Slider s || string.IsNullOrWhiteSpace(s.AutomationId))
                return;

            string path = s.AutomationId;

            try
            {
                if (_session is null)
                {
                    GuardRail.Text = "No active dyno session. Select a preset on the Presets tab.";
                    return;
                }

                double raw = e.NewValue;
                (double coerced, string text) = CoerceAndFormat(path, raw);

                // Do NOT touch s.Value here.
                // Let OnSessionSpecChanged → MirrorAll() push the snapped value back.

                if (_ctrl.TryGetValue(path, out var c))
                    c.entry.Text = text;

                // This is the only place a slider change hits the session
                _session.Set(path, coerced);
            }
            catch (Exception ex)
            {
                GuardRail.Text = "Slider change failed: " + ex.Message;
            }
        }

        private void OnEntryCompleted(object sender, EventArgs e) => CommitEntry(sender as Entry);
        private void OnEntryUnfocused(object sender, FocusEventArgs e) => CommitEntry(sender as Entry);

        private void CommitEntry(Entry? entry)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.AutomationId)) return;
            if (_updatingUI) return;

            if (_session is null)
            {
                GuardRail.Text = "No active dyno session. Select a preset on the Presets tab.";
                return;
            }

            string path = entry.AutomationId;
            if (!double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
                return;

            (double coerced, string text) = CoerceAndFormat(path, raw);

            if (_ctrl.TryGetValue(path, out var c))
            {
                _updatingUI = true;
                c.slider.Value = coerced;
                entry.Text = text;
                _updatingUI = false;
            }

            _session.Set(path, coerced);
        }

        // -------------------- Coercion / formatting ------------

        private (double coerced, string text) CoerceAndFormat(string path, double raw)
        {
            var eff = PathConstraints.Effective(path, _session?.Current ?? new EngineModel());

            // snap to step
            double step = eff.Step > 0 ? eff.Step : 1.0;
            double snapped = Math.Round(raw / step) * step;

            // clamp
            if (eff.Min is double min && snapped < min) snapped = min;
            if (eff.Max is double max && snapped > max) snapped = max;

            return (snapped, FormatForStep(snapped, step));
        }

        private static string FormatForStep(double v, double step)
        {
            var inv = CultureInfo.InvariantCulture;
            if (step >= 1) return v.ToString("F0", inv);
            if (step >= 0.1) return v.ToString("F1", inv);
            return v.ToString("F2", inv);
        }

        // -------------------- Plotting -------------------------

        private void CacheSeries(CompareResult res)
        {
            _xsBase = res.Baseline.Points.Select(p => (double)p.Rpm).ToArray();
            _hpBase = res.Baseline.Points.Select(p => p.Hp).ToArray();
            _tqBase = res.Baseline.Points.Select(p => p.TorqueLbFt).ToArray();

            _xsCur = res.Current.Points.Select(p => (double)p.Rpm).ToArray();
            _hpCur = res.Current.Points.Select(p => p.Hp).ToArray();
            _tqCur = res.Current.Points.Select(p => p.TorqueLbFt).ToArray();
        }

        private void RenderPlotToFit()
        {
            if (PlotHost.Width <= 0 || PlotHost.Height <= 0) return;
            if (_xsBase is null || _hpBase is null || _tqBase is null) return;
            if (_xsCur is null || _hpCur is null || _tqCur is null) return;

            double density = DeviceDisplay.MainDisplayInfo.Density;
            int pxW = Math.Max(1, (int)(PlotHost.Width * density));
            int pxH = Math.Max(1, (int)(PlotHost.Height * density));

            var plt = new ScottPlot.Plot();

            // Baseline
            var hpBase = plt.Add.Scatter(_xsBase, _hpBase); hpBase.LegendText = "HP (baseline)"; hpBase.Axes.YAxis = plt.Axes.Left;
            var tqBase = plt.Add.Scatter(_xsBase, _tqBase); tqBase.LegendText = "Torque (baseline)"; tqBase.Axes.YAxis = plt.Axes.Right;

            // Current
            var hpCur = plt.Add.Scatter(_xsCur, _hpCur); hpCur.LegendText = "HP (current)"; hpCur.Axes.YAxis = plt.Axes.Left;
            var tqCur = plt.Add.Scatter(_xsCur, _tqCur); tqCur.LegendText = "Torque (current)"; tqCur.Axes.YAxis = plt.Axes.Right;

            // Labels
            plt.Title($"Dyno — {_specName}");
            plt.Axes.Bottom.Label.Text = "RPM";
            plt.Axes.Left.Label.Text = "Horsepower";
            plt.Axes.Right.Label.Text = "Torque (lb-ft)";
            plt.Legend.IsVisible = true;

            // Axis limits (union of ranges)
            double xMin = Math.Min(_xsBase[0], _xsCur[0]);
            double xMax = Math.Max(_xsBase[^1], _xsCur[^1]);
            double hpMax = Math.Max(_hpBase.Max(), _hpCur.Max());
            double tqMax = Math.Max(_tqBase.Max(), _tqCur.Max());

            plt.Axes.SetLimits(xMin, xMax, 0, Math.Ceiling(hpMax * 1.10));
            plt.Axes.SetLimitsY(0, Math.Ceiling(tqMax * 1.10), plt.Axes.Right);
            plt.Axes.Margins(0, 0);

            var bytes = plt.GetImageBytes(pxW, pxH, ScottPlot.ImageFormat.Png);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DynoImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            });
        }

        private void UpdateMetrics(CompareResult res)
        {
            var m = res.Metrics;
            Metrics.Text =
                $"Peak HP:  {m.BaselinePeakHp.Hp:F1} @ {m.BaselinePeakHp.Rpm}  →  {m.CurrentPeakHp.Hp:F1} @ {m.CurrentPeakHp.Rpm}  (Δ {m.PeakHpGain:+0.0;-0.0;0.0})\n" +
                $"Peak TQ:  {m.BaselinePeakTq.TorqueLbFt:F1} @ {m.BaselinePeakTq.Rpm}  →  {m.CurrentPeakTq.TorqueLbFt:F1} @ {m.CurrentPeakTq.Rpm}  (Δ {m.PeakTqGain:+0.0;-0.0;0.0})\n" +
                $"Mid Avg TQ (2500–4500):  {m.MidAvgTqGain_2500_4500:+0.0;-0.0;0.0} lb-ft";
        }

        // -------------------- Preset toolbar actions ----------

        private async void OnSavePresetClicked(object sender, EventArgs e)
        {
            if (_session?.Current is null)
            {
                await DisplayAlert("Nothing to save", "Load and modify an engine before saving a preset.", "OK");
                return;
            }

            string? name = await DisplayPromptAsync(
                "Save preset",
                "Preset name:",
                accept: "Save",
                cancel: "Cancel",
                maxLength: 60,
                keyboard: Keyboard.Text);

            if (string.IsNullOrWhiteSpace(name))
                return;

            string? desc = await DisplayPromptAsync(
                "Save preset",
                "Optional description:",
                accept: "OK",
                cancel: "Skip",
                maxLength: 140,
                keyboard: Keyboard.Text);

            var preset = await _presets.SaveUserPresetAsync(_session.Current, name.Trim(), desc);
            _specName = preset.Name;
            Status.Text = $"Saved preset: {preset.Name}";
            await DisplayAlert("Preset saved", $"Saved as \"{preset.Name}\"", "OK");
            RenderPlotToFit();
        }

        private async void OnChangePresetClicked(object sender, EventArgs e)
        {
            await _presets.InitializeAsync();

            if (_presets.Presets.Count == 0)
            {
                await DisplayAlert("No presets", "There are no presets available yet.", "OK");
                return;
            }

            var names = _presets.Presets.Select(p => p.Name).ToArray();
            string? picked = await DisplayActionSheet("Select preset", "Cancel", null, names);

            if (string.IsNullOrWhiteSpace(picked) || picked == "Cancel")
                return;

            var chosen = _presets.Presets.FirstOrDefault(p => p.Name == picked);
            if (chosen is null)
                return;

            _presets.SetCurrentPreset(chosen);
            GuardRail.Text = string.Empty;
            _specName = chosen.Name;

            await SafePlotOnceAsync();
        }

        // -------------------- IO helpers -----------------------

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
            catch { return null; }
        }

        private static string NormalizeForFixedCR(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            var toggles = (root["toggles"] as JsonObject) ?? new JsonObject();
            toggles["compressionBehavior"] = "fixedCR";
            root["toggles"] = toggles;
            return root.ToJsonString();
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}