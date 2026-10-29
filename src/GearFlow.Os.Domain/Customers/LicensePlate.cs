using System.Text.RegularExpressions;
using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Customers;

public static partial class LicensePlate
{
    public static bool TryNormalize(string? raw, out string normalized, out Error error)
    {
        normalized = new string((raw ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (!PlatePattern().IsMatch(normalized))
        {
            error = Error.Validation("Vehicle.Plate", "Placa inválida. Use o padrão antigo ou Mercosul.");
            return false;
        }

        error = Error.None;
        return true;
    }

    [GeneratedRegex("^[A-Z]{3}[0-9]{4}$|^[A-Z]{3}[0-9][A-Z][0-9]{2}$")]
    private static partial Regex PlatePattern();
}
