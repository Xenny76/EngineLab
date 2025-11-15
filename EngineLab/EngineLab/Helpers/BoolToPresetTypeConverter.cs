using System.Globalization;

namespace EngineLab.Helpers
{
    public sealed class BoolToPresetTypeConverter : IValueConverter
    {
        public static BoolToPresetTypeConverter Instance { get; } = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isBuiltIn)
                return isBuiltIn ? "Built-in" : "User preset";
            return "Unknown";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}