using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TamoJuntoGames.API.Exceptions;
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
        private readonly ILogger<UsuarioService> _logger;

        public UsuarioService(AppDbContext context, ITokenService tokenService, ILogger<UsuarioService> logger)
        {
            _context = context;
            _tokenService = tokenService;
            _logger = logger;
        }

        // Busca usuário por Id (sem senha)
        public async Task<UsuarioRespostaDTO?> ObterPorIdAsync(int id)
        {
            return await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new UsuarioRespostaDTO
                {
                    Id = u.Id,
                    NomeCompleto = u.NomeCompleto,
                    Apelido = u.Apelido,
                    Email = u.Email
                })
                .FirstOrDefaultAsync();
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
                throw new EmailDuplicadoException();

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
            await SalvarComVerificacaoDeEmailAsync(emailNormalizado);

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
                throw new EmailDuplicadoException();

            // Atualiza
            usuario.NomeCompleto = dto.NomeCompleto;
            usuario.Apelido = dto.Apelido;
            usuario.Email = email;
            usuario.EmailNormalizado = emailNormalizado;

            await SalvarComVerificacaoDeEmailAsync(emailNormalizado, id);

            return ParaResposta(usuario);
        }

        private async Task SalvarComVerificacaoDeEmailAsync(string emailNormalizado, int? usuarioId = null)
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException
                { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 })
            {
                // O código identifica UNIQUE, não o índice. Confirmamos o conflito de domínio.
                // A consulta não prova qual índice originou a falha se houver outros UNIQUE.
                var emailJaExiste = false;
                try
                {
                    emailJaExiste = await _context.Usuarios.AnyAsync(usuario =>
                        usuario.EmailNormalizado == emailNormalizado &&
                        (usuarioId == null || usuario.Id != usuarioId));
                }
                catch (Exception falhaConfirmacao)
                {
                    // Restrito à consulta após UNIQUE: preserva a falha original no throw abaixo.
                    // Não registrar a exceção/mensagem, SQL ou valores do usuário.
                    _logger.LogWarning(new EventId(1001, "FalhaConfirmacaoDuplicidadeEmail"),
                        "Falha na confirmação de duplicidade após UNIQUE. Tipo secundário: {TipoExcecao}.",
                        falhaConfirmacao.GetType().FullName);
                }

                if (emailJaExiste)
                    throw new EmailDuplicadoException(ex);

                throw;
            }
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
