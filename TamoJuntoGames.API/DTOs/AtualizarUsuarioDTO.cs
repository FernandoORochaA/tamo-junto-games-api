using System.ComponentModel.DataAnnotations;

namespace TamoJuntoGames.API.DTOs
{
    // Para atualizar os dados de um usuario existente
    public class AtualizarUsuarioDTO
    {
        [Required(ErrorMessage = "Nome completo é obrigatório.")]
        [MaxLength(100, ErrorMessage = "Nome completo deve ter no máximo 100 unidades UTF-16.")]
        public string NomeCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Apelido é obrigatório.")]
        [MaxLength(50, ErrorMessage = "Apelido deve ter no máximo 50 unidades UTF-16.")]
        public string Apelido { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-mail é obrigatório.")]
        [MaxLength(254, ErrorMessage = "E-mail deve ter no máximo 254 unidades UTF-16.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;
    }
}
