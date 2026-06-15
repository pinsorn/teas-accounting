using System.Text.Json;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Loads an embedded comb cell-centre map (field name → printed cell-centre X in PDF points) used to
/// place one digit per real printed cell on a NON-uniform comb (e.g. the 1-4-5-2-1 Thai tax-id grid,
/// where equal division drifts across the dash gaps). Shared by the RD-form fillers.
/// </summary>
internal static class RdCells
{
    public static IReadOnlyDictionary<string, IReadOnlyList<double>> Load(string resource)
    {
        var asm = typeof(RdCells).Assembly;
        using var s = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded cells resource '{resource}' not found.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, double[]>>(s)
            ?? new Dictionary<string, double[]>();
        return raw.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<double>)kv.Value);
    }
}
