using Microsoft.EntityFrameworkCore;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Models;

namespace TamoJuntoGames.API.Services
{
    // Implementação do "contrato" IUsuarioService
    // Aqui fica a regra de negócio e o acesso ao banco (via EF Core).
    public class UsuarioService : IUsuarioService
    {
        private readonly AppDbContext _context;

        public UsuarioService(AppDbContext context)
        {
            _context = context;
        }

        // Lista todos os usuários (sem senha)
        public async Task<IEnumerable<UsuarioRespostaDTO>> ListarAsync()
        {
            // 1) Busca no banco
            var usuarios = await _context.Usuarios
                .AsNoTracking()
                .ToListAsync();

            // 2) Converte para DTO fora do SQL
            return usuarios.Select(ParaResposta);
        }

        // Busca usuário por Id (sem senha)
        public async Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id)
        {
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);

            if (usuario == null)
                return null;

            return ParaResposta(usuario);
        }

        // Cria usuário (com hash de senha)
        public async Task<UsuarioRespostaDTO> CriarAsync(CriarUsuarioDTO dto)
        {
            // Regra de negócio: confirmar email
            if (dto.Email != dto.ConfirmarEmail)
                throw new InvalidOperationException("Os e-mails não coincidem.");

            // Regra de negócio: confirmar senha
            if (dto.Senha != dto.ConfirmarSenha)
                throw new InvalidOperationException("As senhas não coincidem.");

            // Regra de negócio: email repetido
            var emailJaExiste = await _context.Usuarios
                .AnyAsync(u => u.Email == dto.Email);

            if (emailJaExiste)
                throw new InvalidOperationException("Já existe um usuário cadastrado com esse e-mail.");

            // Cria o usuário (senha vira hash)
            var novoUsuario = new Usuario
            {
                NomeCompleto = dto.NomeCompleto,
                Apelido = dto.Apelido,
                Email = dto.Email,
                Senha = BCrypt.Net.BCrypt.HashPassword(dto.Senha)
            };

            _context.Usuarios.Add(novoUsuario);
            await _context.SaveChangesAsync();

            return ParaResposta(novoUsuario);
        }

        // Atualiza usuário (sem mexer em senha)
        public async Task<UsuarioRespostaDTO?> AtualizarAsync(int id, AtualizarUsuarioDTO dto)
        {
            var usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Id == id);

            if (usuario == null)
                return null;

            // Regra de negócio: email repetido em outro usuário
            var emailJaExiste = await _context.Usuarios
                .AnyAsync(u => u.Email == dto.Email && u.Id != id);

            if (emailJaExiste)
                throw new InvalidOperationException("Já existe outro usuário com esse e-mail.");

            // Atualiza
            usuario.NomeCompleto = dto.NomeCompleto;
            usuario.Apelido = dto.Apelido;
            usuario.Email = dto.Email;

            await _context.SaveChangesAsync();

            return ParaResposta(usuario);
        }

        // Deleta usuário
        public async Task<bool> DeletarAsync(int id)
        {
            var usuario = await _context.Usuarios.FindAsync(id);

            if (usuario == null)
                return false;

            _context.Usuarios.Remove(usuario);
            await _context.SaveChangesAsync();

            return true;
        }

        // Login (valida hash)
        public async Task<UsuarioRespostaDTO?> LoginAsync(LoginUsuarioDTO dto)
        {
            // Busca por email
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (usuario == null)
                return null;

            // Confere senha digitada vs hash salvo
            var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.Senha);
            if (!senhaValida)
                return null;

            return ParaResposta(usuario);
        }

        // Converte Model -> DTO de resposta (sem senha)
        private static UsuarioRespostaDTO ParaResposta(Usuario usuario)
        {
            return new UsuarioRespostaDTO
            {
                Id = usuario.Id,
                NomeCompleto = usuario.NomeCompleto,
                Apelido = usuario.Apelido,
                Email = usuario.Email
            };
        }
    }
}