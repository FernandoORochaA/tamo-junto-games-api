namespace TamoJuntoGames.API.Models
{
    public class Usuario
    {
        public int Id { get; set; }
        public string NomeCompleto { get; set; } = string.Empty;
        public string Apelido { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string EmailNormalizado { get; set; } = string.Empty;
        public DateTime? DataNascimento { get; set; }
        public string? Genero { get; set; }
        public string Senha { get; set; } = string.Empty;
    }
}
