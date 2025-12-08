namespace TamoJuntoGames.API.DTOs
{
    // Para atualizar os dados de um usuario existente
    public class AtualizarUsuarioDTO
    {
        public string NomeCompleto { get; set; } = string.Empty;
        public string Apelido { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}