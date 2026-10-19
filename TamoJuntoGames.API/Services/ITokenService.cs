namespace TamoJuntoGames.API.Services;

public interface ITokenService
{
    (string Token, DateTime ExpiraEm) GerarToken(
        int usuarioId,
        string email,
        string apelido);
}
