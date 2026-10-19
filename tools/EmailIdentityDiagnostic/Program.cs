using TamoJuntoGames.EmailIdentityDiagnostic;

// Não construir o host da API nem carregar suas configurações/User Secrets.
return await DiagnosticCommand.ExecuteAsync(args, Console.Out, Console.Error);
