using System;

namespace TamoJuntoGames.API.DTOs
{
    public class LoginRespostaDTO
    {
        public UsuarioRespostaDTO Usuario { get; set; } = default!;
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiraEm { get; set; }
    }
}