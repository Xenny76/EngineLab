using System.Text;

namespace EngineLab.Helpers;

public static class Embedded
{
    public static string LoadText(string fileName)
    {
        var asm = typeof(App).Assembly; // MAUI app assembly
        // Find by suffix so we don't care about namespace/folder changes
        string? resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resName is null)
        {
            // Helpful debug dump:
            var all = string.Join(Environment.NewLine, asm.GetManifestResourceNames());
            throw new FileNotFoundException(
                $"Embedded resource '{fileName}' not found.\n" +
                $"Ensure Build Action = EmbeddedResource and path is under the MAUI app project.\n" +
                $"Known resources:\n{all}");
        }

        using Stream s = asm.GetManifestResourceStream(resName)!;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}