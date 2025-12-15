using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Models;

namespace TamoJuntoGames.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsuariosController : ControllerBase
    {
        // DbContext injetado — agora o "banco" é o SQLite via EF Core (não é mais lista em memória)
        private readonly AppDbContext _context;

        public UsuariosController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/usuarios
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UsuarioRespostaDTO>>> GetTodos()
        {
            // 1. Buscar usuários no banco (SQLite)
            var usuarios = await _context.Usuarios
                .AsNoTracking()
                .ToListAsync();

            // 2. Converter para DTO (sem senha) fora do SQL
            var resposta = usuarios
                .Select(ParaResposta)
                .ToList();

            return Ok(resposta);
        }

        // GET: api/usuarios/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UsuarioRespostaDTO>> GetPorId(int id)
        {
            // 1. Buscar usuário no banco
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);

            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 2. Converter para DTO fora do SQL
            var resposta = ParaResposta(usuario);

            return Ok(resposta);
        }

        // DELETE: api/usuarios/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deletar(int id)
        {
            // 1. Localizar o usuario no banco
            var usuario = await _context.Usuarios.FindAsync(id);

            // 2. Caso não localize. 404 (não encontrado)
            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Se achou, remove do banco
            _context.Usuarios.Remove(usuario);

            // 4. Persistir de fato no SQLite
            await _context.SaveChangesAsync();

            // 5. Devolve 204 (sucesso, porém sem corpo na resposta)
            return NoContent();
        }

        // PUT: api/usuarios/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<UsuarioRespostaDTO>> Atualizar(int id, [FromBody] AtualizarUsuarioDTO dto)
        {
            // 1. Procurar o usuário no banco
            var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);

            // 2. Se não achar, devolver 404
            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Verificar se o novo e-mail já não está em uso por OUTRO usuário
            var emailJaExiste = await _context.Usuarios.AnyAsync(u => u.Email == dto.Email && u.Id != id);
            if (emailJaExiste)
                return Conflict(new { mensagem = "Já existe outro usuário com esse e-mail." });

            // 4. Atualizar os dados do usuário encontrado
            usuario.NomeCompleto = dto.NomeCompleto;
            usuario.Apelido = dto.Apelido;
            usuario.Email = dto.Email;

            // 5. Persistir no SQLite
            await _context.SaveChangesAsync();

            // 6. Montar DTO de resposta (sem senha)
            var resposta = ParaResposta(usuario);

            // 7. Devolver 200 OK com os dados atualizados
            return Ok(resposta);
        }

        // POST: api/usuarios
        [HttpPost]
        public async Task<ActionResult<UsuarioRespostaDTO>> Criar([FromBody] CriarUsuarioDTO dto)
        {
            // 1. Validar email
            if (dto.Email != dto.ConfirmarEmail)
                return BadRequest(new { mensagem = "Os e-mails não coincidem." });

            // 2. Validar senha
            if (dto.Senha != dto.ConfirmarSenha)
                return BadRequest(new { mensagem = "As senhas não coincidem." });

            // 3. Validar email repetido no BANCO (não é mais lista)
            var emailJaExiste = await _context.Usuarios.AnyAsync(u => u.Email == dto.Email);
            if (emailJaExiste)
                return Conflict(new { mensagem = "Já existe um usuário cadastrado com esse e-mail." });

            // 4. Converter DTO -> Model
            var novoUsuario = new Usuario
            {
                // Id não é setado: o SQLite/EF gera
                NomeCompleto = dto.NomeCompleto,
                Apelido = dto.Apelido,
                Email = dto.Email,
                Senha = dto.Senha
            };

            // 5. Salvar no banco
            _context.Usuarios.Add(novoUsuario);

            // 6. Persistir de fato no SQLite (aqui o Id é gerado)
            await _context.SaveChangesAsync();

            // 7. Montar DTO de resposta (sem senha)
            var resposta = ParaResposta(novoUsuario);

            // 8. Retornar CREATED (201) com o DTO
            return CreatedAtAction(
                nameof(GetPorId),
                new { id = novoUsuario.Id },
                resposta
            );
        }

        // POST: api/usuarios/login
        [HttpPost("login")]
        public async Task<ActionResult<UsuarioRespostaDTO>> Login([FromBody] LoginUsuarioDTO dto)
        {
            // 1. Validar se veio email e senha
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Senha))
                return BadRequest(new { mensagem = "E-mail e senha são obrigatórios." });

            // 2. Procurar usuário com esse email e senha no BANCO
            // (Fase 1: senha em texto, mais pra frente a gente troca por hash)
            var usuario = await _context.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == dto.Email && u.Senha == dto.Senha);

            // 3. Se não encontrar, devolve 401 (não autorizado)
            if (usuario == null)
                return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

            // 4. Se encontrar, monta DTO de resposta (sem senha)
            var resposta = ParaResposta(usuario);

            // 5. Devolve 200 OK com os dados do usuário logado
            return Ok(resposta);
        }

        // Converte o Model Usuario para o DTO de resposta
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