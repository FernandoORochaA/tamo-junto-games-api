using System.ComponentModel.DataAnnotations;
using TamoJuntoGames.API.Validations;

namespace TamoJuntoGames.API.DTOs
{
    // DTO = o que o FRONT envia para a API na hora do cadastro
    public class CriarUsuarioDTO
    {
        [Required(ErrorMessage = "Nome completo é obrigatório.")]
        public string NomeCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "Apelido é obrigatório.")]
        public string Apelido { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-mail é obrigatório.")]
        [EmailAddress(ErrorMessage = "E-mail inválido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirmação de e-mail é obrigatória.")]
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
