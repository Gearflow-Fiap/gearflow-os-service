using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using GearFlow.Os.Api.Auth;
using GearFlow.Os.Api.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Os.Tests;

public class AuthTests
{
    private static readonly AuthOptions Auth = new() { Username = "recepcao", Password = "senha-de-teste-123" };

    private static readonly JwtOptions Jwt = new()
    {
        Secret = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "GearFlow.Api",
        Audience = "GearFlow.Client"
    };

    [Fact]
    public void Credencial_correta_e_aceita()
    {
        CredentialValidator.IsValid(Auth, "recepcao", "senha-de-teste-123").Should().BeTrue();
    }

    [Theory]
    [InlineData("recepcao", "senha-errada")]
    [InlineData("outro", "senha-de-teste-123")]
    [InlineData("RECEPCAO", "senha-de-teste-123")]
    [InlineData("recepcao", "")]
    [InlineData(null, "senha-de-teste-123")]
    [InlineData("recepcao", null)]
    public void Credencial_incorreta_e_recusada(string? username, string? password)
    {
        CredentialValidator.IsValid(Auth, username, password).Should().BeFalse();
    }

    [Fact]
    public void Sem_credencial_configurada_ninguem_entra()
    {
        var empty = new AuthOptions();

        empty.IsConfigured.Should().BeFalse();
        CredentialValidator.IsValid(empty, "", "").Should().BeFalse();
    }

    [Theory]
    [InlineData("recepcao", "")]
    [InlineData("", "senha")]
    public void Configuracao_pela_metade_impede_a_subida(string username, string password)
    {
        var options = new AuthOptions { Username = username, Password = password };

        var act = options.EnsureConsistent;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validade_do_token_precisa_ser_positiva()
    {
        var options = new AuthOptions { Username = "recepcao", Password = "senha", TokenMinutes = 0 };

        var act = options.EnsureConsistent;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Token_emitido_passa_na_mesma_validacao_da_api()
    {
        var now = DateTime.UtcNow;

        var issued = TokenIssuer.Issue(Jwt, "recepcao", TimeSpan.FromMinutes(60), now);

        issued.TokenType.Should().Be("Bearer");
        issued.ExpiresIn.Should().Be(3600);
        issued.ExpiresAtUtc.Should().Be(now.AddMinutes(60));

        var principal = new JwtSecurityTokenHandler().ValidateToken(issued.AccessToken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Secret)),
            ValidIssuer = Jwt.Issuer,
            ValidAudience = Jwt.Audience,
            ClockSkew = TimeSpan.Zero
        }, out _);

        principal.Should().NotBeNull();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        token.Subject.Should().Be("recepcao");
        token.Claims.Should().Contain(claim => claim.Type == "actor_type" && claim.Value == "staff");
    }

    [Fact]
    public void Token_assinado_com_outra_chave_e_recusado()
    {
        var issued = TokenIssuer.Issue(Jwt, "recepcao", TimeSpan.FromMinutes(60), DateTime.UtcNow);

        var act = () => new JwtSecurityTokenHandler().ValidateToken(issued.AccessToken, new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("outra-chave-com-pelo-menos-32-caracteres!!")),
            ValidIssuer = Jwt.Issuer,
            ValidAudience = Jwt.Audience
        }, out _);

        act.Should().Throw<SecurityTokenException>();
    }
}
