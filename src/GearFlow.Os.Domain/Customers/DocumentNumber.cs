using GearFlow.Os.Domain.Primitives;

namespace GearFlow.Os.Domain.Customers;

public static class DocumentNumber
{
    public static bool TryNormalize(string? raw, out string normalized, out Error error)
    {
        normalized = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (normalized.Distinct().Count() == 1)
        {
            error = Error.Validation("Client.Document", "CPF ou CNPJ inválido.");
            return false;
        }

        var valid = normalized.Length switch
        {
            11 => IsValidCpf(normalized),
            14 => IsValidCnpj(normalized),
            _ => false
        };

        if (!valid)
        {
            error = Error.Validation("Client.Document", "CPF ou CNPJ inválido.");
            return false;
        }

        error = Error.None;
        return true;
    }

    private static bool IsValidCpf(string digits)
    {
        var first = CheckDigit(digits, 10, 9);
        var second = CheckDigit(digits, 11, 10);
        return digits[9] - '0' == first && digits[10] - '0' == second;
    }

    private static bool IsValidCnpj(string digits)
    {
        int[] firstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] secondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        return digits[12] - '0' == Weighted(digits, firstWeights)
            && digits[13] - '0' == Weighted(digits, secondWeights);
    }

    private static int CheckDigit(string digits, int startWeight, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i++)
            sum += (digits[i] - '0') * (startWeight - i);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static int Weighted(string digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
            sum += (digits[i] - '0') * weights[i];
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
