namespace GearFlow.Os.Api.Auth;

/// <summary>
/// Credencial de acesso à API. Os valores vêm de configuração (Auth__Username / Auth__Password),
/// nunca do código: no EKS ficam no Secret gearflow-os-secret, alimentado pelos secrets do GitHub.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public int TokenMinutes { get; init; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Configuração pela metade (só usuário ou só senha) é erro de deploy, não "login desligado".</summary>
    public void EnsureConsistent()
    {
        if (string.IsNullOrWhiteSpace(Username) != string.IsNullOrWhiteSpace(Password))
            throw new InvalidOperationException("Auth:Username e Auth:Password devem ser configurados juntos.");
        if (TokenMinutes <= 0)
            throw new InvalidOperationException("Auth:TokenMinutes deve ser maior que zero.");
    }
}
