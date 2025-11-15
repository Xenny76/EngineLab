using EngineLabLib.Simulation;
using EngineLabLib.Models;
using EngineLabLib.Modification;

namespace EngineLabLib.Session
{
    public sealed class ModSession(EngineModel baseline, SynchronizationContext? uiContext = null)
    {
        public EngineModel Baseline { get; } = baseline;
        public EngineModel Current { get; private set; } = baseline;

        public event Action<EngineModel>? OnSpecChanged;
        public event Action<string, string>? OnGuardRail; // (path,msg)

        private readonly SynchronizationContext? _ui = uiContext; // optional UI marshal

        // --- Single field edit ------------------------------------------------
        public void Set(string path, object value)
            => ApplyInternal([new ModPatch.SetOp(path, value)]);

        // --- Batch edits (one dyno run after many sets) -----------------------
        public void SetMany(IEnumerable<ModPatch.SetOp> sets)
            => ApplyInternal(sets);

        private void ApplyInternal(IEnumerable<ModPatch.SetOp> sets)
        {
            // clamp & apply
            var patch = new ModPatch();
            foreach (var s in sets)
            {
                var (coerced, note) = PathConstraints.Clamp(s.Path, s.Value!, Current);
                if (note == "Disabled for current layout.") continue; // <— skip write
                if (note is not null) OnGuardRail?.Invoke(s.Path, note);
                patch.Sets.Add(new ModPatch.SetOp(s.Path, coerced));
            }

            Current = ModApplier.Apply(Current, patch);

            // geometry-driven CR update
            if (Current.Toggles.CompressionBehavior == CompressionBehavior.GeometryDefinesCR &&
                (Current.ChamberVolume_cc is not null || Current.HeadGasketThickness_mm is not null))
            {
                double cr = GeometryUtils.ComputeCR_FromGeometry(Current);
                Current = ModApplier.Apply(Current, new ModPatch { Sets = { new("CompressionRatio", cr) } });
            }

            DebouncedNotify();
        }

        private void DebouncedNotify()
        {
            // Simple immediate notify – no background Tasks, no cancellation.
            void fire() => OnSpecChanged?.Invoke(Current);

            if (_ui is not null)
                _ui.Post(_ => fire(), null);  // marshal to UI thread
            else
                fire();
        }

        // ---------- Dyno compare seam (frontend calls this) -------------------
        public CompareResult GetComparison(DynoConfig cfg, IDynoRunner runner)
            => DynoCompare.Run(Baseline, Current, cfg, runner);
    }
}