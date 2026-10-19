using System.ComponentModel.DataAnnotations;
using TamoJuntoGames.API.Validations;

namespace TamoJuntoGames.API.DTOs
{
    // DTO = o que o FRONT envia para a API na hora do cadastro
    public class CriarUsuarioDTO
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

        [Required(ErrorMessage = "Confirmação de e-mail é obrigatória.")]
        [MaxLength(254, ErrorMessage = "Confirmação de e-mail deve ter no máximo 254 unidades UTF-16.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        [CamposIguais("Email", ErrorMessage = "Os e-mails não coincidem.")]
        public string ConfirmarEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Senha é obrigatória.")]
        [MinLength(8, ErrorMessage = "A senha deve ter no mínimo 8 caracteres.")]
        public string Senha { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirmação de senha é obrigatória.")]
        [CamposIguais("Senha", ErrorMessage = "As senhas não coincidem.")]
        public string ConfirmarSenha { get; set; } = string.Empty;
    }
}
