using System.Text.Json;
using EngineLabLib.Models;

namespace EngineLabLib.Services
{
    /// <summary>
    /// Manages a list of engine presets (built-in + user).
    /// This class is MAUI-agnostic: it does not know about FileSystem, Embedded, etc.
    /// The app passes:
    ///   - baseFolder (where user presets live)
    ///   - builtInLoader (how to load built-in JSON files)
    /// </summary>
    public sealed class PresetService
    {
        private readonly string _userPresetFolder;
        private readonly string _indexPath;
        private readonly Func<string, Task<string>> _builtInLoader;

        private readonly List<EnginePreset> _presets = new();

        public IReadOnlyList<EnginePreset> Presets => _presets;
        public EnginePreset? CurrentPreset { get; private set; }

        /// <param name="baseFolder">Base folder (from the app, e.g. FileSystem.AppDataDirectory).</param>
        /// <param name="builtInLoader">
        /// Function that loads built-in preset JSON given a file name
        /// (the app can use embedded resources / MAUI assets here).
        /// </param>
        public PresetService(string baseFolder, Func<string, Task<string>> builtInLoader)
        {
            _userPresetFolder = Path.Combine(baseFolder, "Presets");
            Directory.CreateDirectory(_userPresetFolder);

            _indexPath = Path.Combine(_userPresetFolder, "presets.index.json");
            _builtInLoader = builtInLoader;
        }

        public async Task InitializeAsync()
        {
            _presets.Clear();

            // 1) Built-in presets (metadata only; JSON loaded via _builtInLoader)
            _presets.AddRange(GetBuiltInPresets());

            // 2) User presets from index file
            if (File.Exists(_indexPath))
            {
                var json = await File.ReadAllTextAsync(_indexPath);
                var list = JsonSerializer.Deserialize<List<EnginePreset>>(json);
                if (list is not null)
                    _presets.AddRange(list.Where(p => !p.IsBuiltIn));
            }
        }

        private IEnumerable<EnginePreset> GetBuiltInPresets()
        {
            // Add more stock engines here later if you want
            yield return new EnginePreset
            {
                Id = "stock_miata_b6ze",
                Name = "Miata 1.6 B6ZE (Stock)",
                Description = "Baseline stock Miata engine.",
                IsBuiltIn = true,
                JsonFileName = "B6ZE_Default.json"
            };
        }

        private async Task SaveIndexAsync()
        {
            var userPresets = _presets.Where(p => !p.IsBuiltIn).ToList();
            var json = JsonSerializer.Serialize(
                userPresets,
                new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(_indexPath, json);
        }

        public void SetCurrentPreset(EnginePreset? preset)
        {
            CurrentPreset = preset;
        }

        /// <summary>
        /// Load JSON for the current preset (built-in or user).
        /// </summary>
        public async Task<string> LoadCurrentPresetJsonAsync()
        {
            if (CurrentPreset is null)
                throw new InvalidOperationException("Current preset not set.");

            if (CurrentPreset.IsBuiltIn)
            {
                // Let the app handle embedded / MAUI asset logic
                return await _builtInLoader(CurrentPreset.JsonFileName);
            }
            else
            {
                var path = Path.Combine(_userPresetFolder, CurrentPreset.JsonFileName);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"User preset JSON not found: {path}");

                return await File.ReadAllTextAsync(path);
            }
        }

        /// <summary>
        /// Save a user preset given an EngineModel.
        /// JsonLoadSave (in the library) handles the serialization.
        /// </summary>
        public async Task<EnginePreset> SaveUserPresetAsync(
            EngineModel engine,
            string presetName,
            string? description = null)
        {
            var id = Guid.NewGuid().ToString("N");
            var fileName = $"user_{id}.json";

            // Serialize using the shared helper that lives in the library
            string json = JsonLoadSave.ToJson(engine);

            var path = Path.Combine(_userPresetFolder, fileName);
            await File.WriteAllTextAsync(path, json);

            var preset = new EnginePreset
            {
                Id = id,
                Name = presetName,
                Description = description,
                IsBuiltIn = false,
                JsonFileName = fileName,
                BasedOnPresetId = CurrentPreset?.Id
            };

            _presets.Add(preset);
            await SaveIndexAsync();

            return preset;
        }

        public async Task DeleteUserPresetAsync(EnginePreset preset)
        {
            if (preset.IsBuiltIn)
                throw new InvalidOperationException("Cannot delete built-in presets.");

            var path = Path.Combine(_userPresetFolder, preset.JsonFileName);
            if (File.Exists(path))
                File.Delete(path);

            _presets.Remove(preset);
            await SaveIndexAsync();

            if (CurrentPreset?.Id == preset.Id)
                CurrentPreset = _presets.FirstOrDefault();
        }
    }
}