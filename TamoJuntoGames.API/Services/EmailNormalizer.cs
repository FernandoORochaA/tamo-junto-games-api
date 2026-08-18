namespace TamoJuntoGames.API.Services
{
    public static class EmailNormalizer
    {
        public static string ParaApresentacao(string email)
        {
            return email.Trim();
        }

        public static string ParaIdentidade(string email)
        {
            return email.Trim().ToUpperInvariant();
        }
    }
}
