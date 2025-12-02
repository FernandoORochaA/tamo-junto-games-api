using Microsoft.AspNetCore.Mvc;
using TamoJuntoGames.API.Models;
using TamoJuntoGames.API.DTOs;

namespace TamoJuntoGames.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsuariosController : ControllerBase
    {
        // "Banco" em memória — testes
        private static readonly List<Usuario> Usuarios = new();
        private static int _proximoId = 1;

        // GET: api/usuarios
        [HttpGet]
        public ActionResult<IEnumerable<Usuario>> GetTodos()
        {
            return Ok(Usuarios);
        }

        // GET: api/usuarios/{id}
        [HttpGet("{id:int}")]
        public ActionResult<Usuario> GetPorId(int id)
        {
            var usuario = Usuarios.FirstOrDefault(u => u.Id == id);

            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            return Ok(usuario);
        }

        // POST: api/usuarios
        [HttpPost]
        public ActionResult<Usuario> Criar([FromBody] CriarUsuarioDTO dto)
        {
            // 1. Validar email
            if (dto.Email != dto.ConfirmarEmail)
                return BadRequest(new { mensagem = "Os e-mails não coincidem." });

            // 2. Validar senha
            if (dto.Senha != dto.ConfirmarSenha)
                return BadRequest(new { mensagem = "As senhas não coincidem." });

            // 3. Validar email repetido
            var emailJaExiste = Usuarios.Any(u => u.Email == dto.Email);
            if (emailJaExiste)
                return Conflict(new { mensagem = "Já existe um usuário cadastrado com esse e-mail." });

            // 4. Converter DTO -> Model
            var novoUsuario = new Usuario
            {
                Id = _proximoId++,
                NomeCompleto = dto.NomeCompleto,
                Apelido = dto.Apelido,
                Email = dto.Email,
                Senha = dto.Senha
            };

            // 5. "Salvar" na lista
            Usuarios.Add(novoUsuario);

            // 6. Retornar CREATED (201)
            return CreatedAtAction(nameof(GetPorId), new { id = novoUsuario.Id }, novoUsuario);
        }
    }
}