using System.ComponentModel.DataAnnotations;

namespace TamoJuntoGames.API.Validations
{
    // Validação customizada:
    // garante que o valor de um campo seja igual ao valor de outro campo no mesmo DTO.
    // Exemplo: ConfirmarEmail deve ser igual a Email.
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class CamposIguaisAttribute : ValidationAttribute
    {
        private readonly string _nomeDoCampoParaComparar;

        public CamposIguaisAttribute(string nomeDoCampoParaComparar)
        {
            _nomeDoCampoParaComparar = nomeDoCampoParaComparar;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // 1) Pega o objeto inteiro (o DTO)
            var instancia = validationContext.ObjectInstance;

            // 2) Pega a "outra propriedade" (ex: Email)
            var propriedadeParaComparar = validationContext.ObjectType.GetProperty(_nomeDoCampoParaComparar);

            if (propriedadeParaComparar == null)
                return new ValidationResult($"Campo para comparar '{_nomeDoCampoParaComparar}' não existe.");

            // 3) Valor do campo original (ex: Email)
            var valorParaComparar = propriedadeParaComparar.GetValue(instancia)?.ToString();

            // 4) Valor do campo atual (ex: ConfirmarEmail)
            var valorAtual = value?.ToString();

            // 5) Se forem diferentes, falha na validação
            if (!string.Equals(valorAtual, valorParaComparar, StringComparison.Ordinal))
                return new ValidationResult(ErrorMessage);

            return ValidationResult.Success;
        }
    }
}