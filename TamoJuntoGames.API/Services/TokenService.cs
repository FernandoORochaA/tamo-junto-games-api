using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Options;

namespace TamoJuntoGames.API.Services;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public (string Token, DateTime ExpiraEm) GerarToken(
        int usuarioId,
        string email,
        string apelido)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpireMinutes);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("apelido", apelido)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwtToken = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiraEm,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(jwtToken), expiraEm);
    }
}
