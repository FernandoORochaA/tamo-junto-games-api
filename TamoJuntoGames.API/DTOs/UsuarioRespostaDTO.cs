namespace TamoJuntoGames.API.DTOs
{
    public class UsuarioRespostaDTO
    {
        public int Id { get; set; }

        public string NomeCompleto { get; set; } = string.Empty;

        public string Apelido { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
    }
}
