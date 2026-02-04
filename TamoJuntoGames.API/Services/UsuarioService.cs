using System;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        private readonly IConfiguration _config;

        public UsuarioService(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
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
        public async Task<LoginRespostaDTO?> LoginAsync(LoginUsuarioDTO dto)
        {
            // 1 - Busca por email
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (usuario == null)
                return null;

            // 2 - Confere senha digitada vs hash salvo
            var senhaValida = BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.Senha);
            if (!senhaValida)
                return null;

            // 3) Gera token JWT
            var token = GerarToken(usuario, out var expiraEm);


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
        // Gera um JWT para o usuário logado
        private string GerarToken(Usuario usuario, out DateTime expiraEm)
        {
            // Lê configurações do appsettings.json
            var jwtKey = _config["Jwt:Key"];
            var jwtIssuer = _config["Jwt:Issuer"];
            var jwtAudience = _config["Jwt:Audience"];
            var expireMinutes = int.Parse(_config["Jwt:ExpireMinutes"] ?? "60");

            // Validação jwtKey
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("Jwt:Key não configurado no appsettings.json");

            expiraEm = DateTime.UtcNow.AddMinutes(expireMinutes);

            // Claims = "informações" que vão dentro do token (quem é o usuário, etc.)
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
                new Claim("apelido", usuario.Apelido)
            };

            // Monta a assinatura do token com a chave secreta
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Cria o token
            var jwtToken = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiraEm,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }
    }
}