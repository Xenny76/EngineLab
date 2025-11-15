using EngineLabLib.Services;
using Microsoft.Extensions.Logging;
using ScottPlot.Maui;
using EngineLab.Helpers;
using EngineLab.Views;

namespace EngineLab
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseScottPlot()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Shared PresetService singleton for the whole app
            builder.Services.AddSingleton<PresetService>(sp =>
                new PresetService(
                    FileSystem.AppDataDirectory,
                    async fileName =>
                    {
                        string? json = null;

                        // Try embedded first
                        try
                        {
                            json = Embedded.LoadText(fileName);
                        }
                        catch
                        {
                            json = null;
                        }

                        // Then MAUI asset
                        if (json is null)
                        {
                            try
                            {
                                using var s = await FileSystem.OpenAppPackageFileAsync(fileName);
                                using var r = new StreamReader(s);
                                json = await r.ReadToEndAsync();
                            }
                            catch
                            {
                                json = null;
                            }
                        }

                        if (json is null)
                            throw new FileNotFoundException($"{fileName} not found as EmbeddedResource or MAUI asset.");

                        return json;
                    }));

            // Pages that use PresetService via constructor injection
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<PresetsPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}