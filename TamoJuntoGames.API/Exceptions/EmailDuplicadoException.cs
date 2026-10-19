namespace TamoJuntoGames.API.Exceptions;

public sealed class EmailDuplicadoException : Exception
{
    public const string MensagemPublica = "Já existe um usuário cadastrado com esse e-mail.";

    public EmailDuplicadoException(Exception? innerException = null)
        : base(MensagemPublica, innerException)
    {
    }
}
