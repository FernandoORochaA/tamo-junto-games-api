using System;
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
        private readonly ITokenService _tokenService;

        public UsuarioService(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
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
            var email = EmailNormalizer.ParaApresentacao(dto.Email);
            var emailNormalizado = EmailNormalizer.ParaIdentidade(dto.Email);

            // Regra de negócio: email repetido
            var emailJaExiste = await _context.Usuarios
                .AnyAsync(u => u.EmailNormalizado == emailNormalizado);

            if (emailJaExiste)
                throw new InvalidOperationException("Já existe um usuário cadastrado com esse e-mail.");

            // Cria o usuário (senha vira hash)
            var novoUsuario = new Usuario
            {
                NomeCompleto = dto.NomeCompleto,
                Apelido = dto.Apelido,
                Email = email,
                EmailNormalizado = emailNormalizado,
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

            var email = EmailNormalizer.ParaApresentacao(dto.Email);
            var emailNormalizado = EmailNormalizer.ParaIdentidade(dto.Email);

            // Regra de negócio: email repetido em outro usuário
            var emailJaExiste = await _context.Usuarios
                .AnyAsync(u => u.EmailNormalizado == emailNormalizado && u.Id != id);

            if (emailJaExiste)
                throw new InvalidOperationException("Já existe outro usuário com esse e-mail.");

            // Atualiza
            usuario.NomeCompleto = dto.NomeCompleto;
            usuario.Apelido = dto.Apelido;
            usuario.Email = email;
            usuario.EmailNormalizado = emailNormalizado;

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
        public async Task<LoginRespostaDTO?> LoginAsync(LoginUsuarioDTO dto)
        {
            var emailNormalizado = EmailNormalizer.ParaIdentidade(dto.Email);

            // 1 - Busca por email
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EmailNormalizado == emailNormalizado);

            if (usuario == null)
                return null;

            // 2 - Confere senha digitada vs hash salvo
            var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.Senha);
            if (!senhaValida)
                return null;

            // 3) Gera token JWT
            var (token, expiraEm) = _tokenService.GerarToken(
                usuario.Id, usuario.Email, usuario.Apelido);


            // 4) Devolve usuário + token
            return new LoginRespostaDTO
            {
                Usuario = ParaResposta(usuario),
                Token = token,
                ExpiraEm = expiraEm
            };
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
