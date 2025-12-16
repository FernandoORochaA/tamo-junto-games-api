using System.ComponentModel.DataAnnotations;

namespace TamoJuntoGames.API.DTOs
{
    // Para atualizar os dados de um usuario existente
    public class AtualizarUsuarioDTO
    {
        [Required(ErrorMessage = "Nome completo é obrigatório.")]
        public string NomeCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Apelido é obrigatório.")]
        public string Apelido { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;
    }
}