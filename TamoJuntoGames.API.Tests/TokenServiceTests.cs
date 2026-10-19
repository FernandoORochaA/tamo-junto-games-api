using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Options;
using TamoJuntoGames.API.Services;
using Xunit;

namespace TamoJuntoGames.API.Tests;

public class TokenServiceTests
{
    [Fact]
    public void GerarTokenPreservaAssinaturaClaimsEExpiracaoSemBancoDeDados()
    {
        var options = new JwtOptions
        {
            Key = new string('é', 16),
            Issuer = "TokenService.Tests",
            Audience = "TokenService.Tests.Client",
            ExpireMinutes = 17
        };
        var service = new TokenService(Microsoft.Extensions.Options.Options.Create(options));
        var before = DateTime.UtcNow;

        var result = service.GerarToken(42, "Jogador@Example.com", "JogadorTeste");

        var after = DateTime.UtcNow;
        Assert.Equal(DateTimeKind.Utc, result.ExpiraEm.Kind);
        Assert.InRange(result.ExpiraEm,
            before.AddMinutes(options.ExpireMinutes), after.AddMinutes(options.ExpireMinutes));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.Token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero
        }, out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal(options.Issuer, jwt.Issuer);
        Assert.Equal(options.Audience, Assert.Single(jwt.Audiences));
        Assert.Equal("42", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal("Jogador@Example.com", principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal("JogadorTeste", principal.FindFirst("apelido")?.Value);
        Assert.InRange((result.ExpiraEm - jwt.ValidTo).TotalSeconds, 0, 1);
    }
}
