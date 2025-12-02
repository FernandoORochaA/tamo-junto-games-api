namespace TamoJuntoGames.API.DTOs
{
    // DTO = o que o FRONT envia para a API na hora do cadastro
    public class CriarUsuarioDTO
    {
        public string NomeCompleto { get; set; } = string.Empty;
        public string Apelido { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ConfirmarEmail { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public string ConfirmarSenha { get; set; } = string.Empty;
    }
}
