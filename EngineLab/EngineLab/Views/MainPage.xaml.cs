using System.Text.Json.Nodes;
using EngineLab.Helpers;
using EngineLabLib.Services;
using EngineLabLib.Simulation;
using EngineLabLib.Models;
using EngineLabLib.Modification;
using EngineLabLib.Session;

namespace EngineLab.Views;

public partial class DynoPage : ContentPage
{
    private bool _plotted;

    // session + dyno
    private ModSession? _session;
    private DynoConfig _cfg = new();
    private readonly SimulationModel _runner = new() { DrivetrainLossFraction = 0.15 };

    // cached series for correct pixel-size rendering
    private double[]? _xs;
    private double[]? _hpBase, _tqBase;
    private double[]? _hpCur, _tqCur;
    private string _specName = "";

    // UI update guard (avoid feedback loop when we set Slider.Value from code)
    private bool _updatingUI;

    public DynoPage()
    {
        InitializeComponent();
        PlotHost.SizeChanged += (_, __) => RenderPlotToFit();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_plotted) return;
        _plotted = true;
        _ = PlotOnceAsync();
    }

    private async Task PlotOnceAsync()
    {
        try
        {
            Status.Text = "Loading preset…";

            // 1) Try embedded, then MAUI asset
            string json = TryLoadEmbedded("B6ZE_Default.json")
                          ?? await TryLoadMauiAssetAsync("B6ZE_Default.json")
                          ?? throw new FileNotFoundException("B6ZE_Default.json not found as EmbeddedResource or MAUI asset.");

            // 2) Normalize toggles to avoid CR validation blocking
            json = NormalizeForFixedCR(json);

            // 3) Deserialize + validate
            var spec = JsonLoadSave.FromJson(json);
            _specName = spec.Name ?? "";

            // 4) Start a ModSession
            _session = new ModSession(spec, SynchronizationContext.Current);
            _session.OnSpecChanged += OnSessionSpecChanged;
            _session.OnGuardRail += (path, msg) =>
                MainThread.BeginInvokeOnMainThread(() => GuardRail.Text = $"{path}: {msg}");

            // 5) Dyno config (lb-ft model)
            _cfg = new DynoConfig
            {
                RpmStart = 1500,
                RpmStop = spec.RevLimit_RPM,
                StepRpm = 50,
                WheelBasis = true
            };

            // 6) Initial comparison (baseline vs baseline)
            var res0 = _session.GetComparison(_cfg, _runner);
            CacheSeries(res0);
            SetupControls(spec);
            UpdateControlReadouts(spec);
            UpdateMetrics(res0);
            RenderPlotToFit();

            Status.Text = "OK";
        }
        catch (Exception ex)
        {
            Content = new ScrollView
            {
                Content = new Label
                {
                    Text = "Dyno failed:\n\n" + ex,
                    TextColor = Colors.Red,
                    Padding = 16
                }
            };
        }
    }

    // ---- Session events ------------------------------------------------------

    private void OnSessionSpecChanged(EngineModel cur)
    {
        // compute comparison using same cfg/runner
        var res = _session!.GetComparison(_cfg, _runner);
        CacheSeries(res);

        // reflect coerced values (after clamps) back into sliders/labels
        _updatingUI = true;
        try
        {
            SliderThrottle.Value = cur.ThrottleDiameter_mm;
            SliderIntakeDur.Value = cur.Cam.IntakeDuration_deg050;
            UpdateControlReadouts(cur);
        }
        finally { _updatingUI = false; }

        UpdateMetrics(res);
        RenderPlotToFit();
    }

    // ---- Plot helpers --------------------------------------------------------

    private void CacheSeries(CompareResult res)
    {
        _xs = res.Baseline.Points.Select(p => (double)p.Rpm).ToArray();

        _hpBase = res.Baseline.Points.Select(p => p.Hp).ToArray();
        _tqBase = res.Baseline.Points.Select(p => p.TorqueLbFt).ToArray();

        _hpCur = res.Current.Points.Select(p => p.Hp).ToArray();
        _tqCur = res.Current.Points.Select(p => p.TorqueLbFt).ToArray();
    }

    private void RenderPlotToFit()
    {
        if (PlotHost.Width <= 0 || PlotHost.Height <= 0) return;
        if (_xs is null || _hpBase is null || _tqBase is null || _hpCur is null || _tqCur is null) return;

        double density = DeviceDisplay.MainDisplayInfo.Density;
        int pxW = Math.Max(1, (int)(PlotHost.Width * density));
        int pxH = Math.Max(1, (int)(PlotHost.Height * density));

        var plt = new ScottPlot.Plot();

        // Baseline (left: HP, right: TQ)
        var hpBase = plt.Add.Scatter(_xs, _hpBase);
        hpBase.LegendText = "HP (baseline)";
        hpBase.Axes.YAxis = plt.Axes.Left;

        var tqBase = plt.Add.Scatter(_xs, _tqBase);
        tqBase.LegendText = "Torque (baseline)";
        tqBase.Axes.YAxis = plt.Axes.Right;

        // Current (left: HP, right: TQ)
        var hpCur = plt.Add.Scatter(_xs, _hpCur);
        hpCur.LegendText = "HP (current)";
        hpCur.Axes.YAxis = plt.Axes.Left;

        var tqCur = plt.Add.Scatter(_xs, _tqCur);
        tqCur.LegendText = "Torque (current)";
        tqCur.Axes.YAxis = plt.Axes.Right;

        // Labels
        plt.Title($"Dyno — {_specName}");
        plt.Axes.Bottom.Label.Text = "RPM";
        plt.Axes.Left.Label.Text = "Horsepower";
        plt.Axes.Right.Label.Text = "Torque (lb-ft)";

        // Axis limits
        double hpMax = Math.Max(_hpBase.Max(), _hpCur.Max());
        double tqMax = Math.Max(_tqBase.Max(), _tqCur.Max());
        plt.Axes.SetLimits(_xs[0], _xs[^1], 0, Math.Ceiling(hpMax * 1.10));       // left (HP)
        plt.Axes.SetLimitsY(0, Math.Ceiling(tqMax * 1.10), plt.Axes.Right);       // right (TQ)
        plt.Axes.Margins(0, 0);

        var bytes = plt.GetImageBytes(pxW, pxH, ScottPlot.ImageFormat.Png);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DynoImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        });
    }

    // ---- Controls ------------------------------------------------------------

    private void SetupControls(EngineModel cur)
    {
        // Use constraints to set ranges/steps
        var cThrottle = PathConstraints.Effective("ThrottleDiameter_mm", cur);
        SliderThrottle.Minimum = cThrottle.Min ?? 30;
        SliderThrottle.Maximum = cThrottle.Max ?? 80;
        SliderThrottle.Value = cur.ThrottleDiameter_mm;

        var cIntDur = PathConstraints.Effective("Cam.IntakeDuration_deg050", cur);
        SliderIntakeDur.Minimum = cIntDur.Min ?? 190;
        SliderIntakeDur.Maximum = cIntDur.Max ?? 320;
        SliderIntakeDur.Value = cur.Cam.IntakeDuration_deg050;
    }

    private void UpdateControlReadouts(EngineModel cur)
    {
        ValThrottle.Text = $"{cur.ThrottleDiameter_mm:F0} mm";
        ValIntakeDur.Text = $"{cur.Cam.IntakeDuration_deg050:F0}°";
    }

    private void UpdateMetrics(CompareResult res)
    {
        var m = res.Metrics;
        Metrics.Text =
            $"Peak HP:  {m.BaselinePeakHp.Hp:F1} @ {m.BaselinePeakHp.Rpm}  →  {m.CurrentPeakHp.Hp:F1} @ {m.CurrentPeakHp.Rpm}  (Δ {m.PeakHpGain:+0.0;-0.0;0.0})\n" +
            $"Peak TQ:  {m.BaselinePeakTq.TorqueLbFt:F1} @ {m.BaselinePeakTq.Rpm}  →  {m.CurrentPeakTq.TorqueLbFt:F1} @ {m.CurrentPeakTq.Rpm}  (Δ {m.PeakTqGain:+0.0;-0.0;0.0})\n" +
            $"Mid Avg TQ (2500–4500):  {m.MidAvgTqGain_2500_4500:+0.0;-0.0;0.0} lb-ft";
    }

    // ---- Slider handlers -----------------------------------------------------

    private void OnThrottleChanged(object sender, ValueChangedEventArgs e)
    {
        if (_updatingUI) return;

        double step = (_session is null) ? 1.0
            : PathConstraints.Effective("ThrottleDiameter_mm", _session.Current).Step;

        double snapped = step > 0 ? Math.Round(e.NewValue / step) * step : e.NewValue;

        // live label matches coerced value
        ValThrottle.Text = step >= 1 ? $"{snapped:F0} mm" : $"{snapped:F2} mm";

        // keep the thumb on what we're actually sending
        if (Math.Abs(snapped - e.NewValue) > 1e-6)
        {
            _updatingUI = true;
            SliderThrottle.Value = snapped;
            _updatingUI = false;
        }

        _session?.Set("ThrottleDiameter_mm", snapped);
    }

    private void OnIntakeDurChanged(object sender, ValueChangedEventArgs e)
    {
        if (_updatingUI) return;

        double step = (_session is null) ? 1.0
            : PathConstraints.Effective("Cam.IntakeDuration_deg050", _session.Current).Step;

        double snapped = step > 0 ? Math.Round(e.NewValue / step) * step : e.NewValue;

        ValIntakeDur.Text = step >= 1 ? $"{snapped:F0}°" : $"{snapped:F2}°";

        if (Math.Abs(snapped - e.NewValue) > 1e-6)
        {
            _updatingUI = true;
            SliderIntakeDur.Value = snapped;
            _updatingUI = false;
        }

        _session?.Set("Cam.IntakeDuration_deg050", snapped);
    }

    // ---- Helpers -------------------------------------------------------------

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
        toggles["compressionBehavior"] = "fixedCR"; // camelCase enum per your converter
        root["toggles"] = toggles;
        return root.ToJsonString();
    }
}