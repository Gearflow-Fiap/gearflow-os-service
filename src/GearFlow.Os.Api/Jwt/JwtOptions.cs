using System.ComponentModel.DataAnnotations;

namespace GearFlow.Os.Api.Jwt;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "Jwt:Secret deve ter no mínimo 32 caracteres (HMAC-SHA256).")]
    public string Secret { get; init; } = string.Empty;

    public string Issuer { get; init; } = "GearFlow.Api";

    public string Audience { get; init; } = "GearFlow.Client";
}
