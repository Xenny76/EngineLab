using System.Collections.ObjectModel;
using EngineLabLib.Models;
using EngineLabLib.Services;

namespace EngineLab.Views
{
    public partial class PresetsPage : ContentPage
    {
        private readonly PresetService _presets;

        public ObservableCollection<EnginePreset> Presets { get; } = new();

        public PresetsPage(PresetService presets)
        {
            InitializeComponent();
            _presets = presets;
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Coming back to Presets = "forget the dyno’s current preset".
            _presets.SetCurrentPreset(null);

            try
            {
                StatusLabel.Text = "Loading presets…";

                await _presets.InitializeAsync();

                Presets.Clear();
                foreach (var p in _presets.Presets)
                    Presets.Add(p);

                StatusLabel.Text = Presets.Count == 0
                    ? "No presets found."
                    : $"{Presets.Count} presets loaded.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Failed to load presets: " + ex.Message;
            }
        }

        private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not EnginePreset preset)
                return;

            _presets.SetCurrentPreset(preset);
            StatusLabel.Text = $"Selected: {preset.Name}. Switch to the Dyno tab to view.";

            ((CollectionView)sender).SelectedItem = null;
        }

        private async void OnDeletePresetClicked(object sender, EventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.BindingContext is not EnginePreset preset)
                return;

            // Extra safety: should never be visible for built-ins
            if (preset.IsBuiltIn)
            {
                await DisplayAlert("Built-in preset", "Built-in presets cannot be deleted.", "OK");
                return;
            }

            bool confirm = await DisplayAlert(
                "Delete preset?",
                $"Are you sure you want to delete '{preset.Name}'? This cannot be undone.",
                "Delete",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                // Use your existing service method
                await _presets.DeleteUserPresetAsync(preset);

                // Update the observable collection bound to the CollectionView
                Presets.Remove(preset);

                StatusLabel.Text = Presets.Count == 0
                    ? "No presets found."
                    : $"{Presets.Count} presets loaded.";
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to delete preset:\n{ex.Message}", "OK");
            }
        }
    }
}