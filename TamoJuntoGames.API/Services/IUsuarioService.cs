using TamoJuntoGames.API.DTOs;

namespace TamoJuntoGames.API.Services
{
    // "Contrato" do Service de Usuários:
    // aqui é definido o que o SERVICE faz (sem dizer COMO faz).
    public interface IUsuarioService
    {
        // Lista todos os usuários (retorna DTO de resposta, sem senha)
        Task<IEnumerable<UsuarioRespostaDTO>> ListarAsync();

        // Busca um usuário por Id (se não existir, retorna null)
        Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id);

        // Cria um usuário (retorna o usuário criado já como DTO)
        Task<UsuarioRespostaDTO> CriarAsync(CriarUsuarioDTO dto);

        // Atualiza um usuário existente (se não existir, retorna null)
        Task<UsuarioRespostaDTO?> AtualizarAsync(int id, AtualizarUsuarioDTO dto);

        // Deleta um usuário (retorna true se deletou, false se não encontrou)
        Task<bool> DeletarAsync(int id);

        // Login (retorna DTO se ok; null se credenciais inválidas)
        Task<UsuarioRespostaDTO?> LoginAsync(LoginUsuarioDTO dto);
    }
}