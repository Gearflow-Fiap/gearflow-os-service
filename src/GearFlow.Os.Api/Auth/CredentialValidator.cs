using System.Security.Cryptography;
using System.Text;

namespace GearFlow.Os.Api.Auth;

public static class CredentialValidator
{
    /// <summary>
    /// Compara usuário e senha em tempo constante. Os dois lados viram hash SHA-256 antes da comparação,
    /// para que nem o tamanho da senha vaze pelo tempo de resposta.
    /// </summary>
    public static bool IsValid(AuthOptions options, string? username, string? password)
    {
        if (!options.IsConfigured || username is null || password is null)
            return false;

        var userOk = FixedTimeEquals(username, options.Username);
        var passwordOk = FixedTimeEquals(password, options.Password);
        return userOk & passwordOk;
    }

    private static bool FixedTimeEquals(string informed, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(informed)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
