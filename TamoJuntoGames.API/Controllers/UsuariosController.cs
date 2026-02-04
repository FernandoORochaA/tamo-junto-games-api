using Microsoft.AspNetCore.Mvc;
using TamoJuntoGames.API.DTOs;
using TamoJuntoGames.API.Services;
using Microsoft.AspNetCore.Authorization;

namespace TamoJuntoGames.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Tudo protegido por padrão
    public class UsuariosController : ControllerBase
    {
        // Service injetado — agora o "cérebro" fica no UsuarioService
        private readonly IUsuarioService _usuarioService;

        public UsuariosController(IUsuarioService usuarioService)
        {
            _usuarioService = usuarioService;
        }

        // GET: api/usuarios
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UsuarioRespostaDTO>>> GetTodos()
        {
            // 1. Pede pro Service listar os usuários
            var resposta = await _usuarioService.ListarAsync();

            // 2. Devolve 200 OK
            return Ok(resposta);
        }

        // GET: api/usuarios/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UsuarioRespostaDTO>> GetPorId(int id)
        {
            // 1. Pede pro Service buscar por Id
            var resposta = await _usuarioService.ObterPorIdAsync(id);

            // 2. Se não existir, 404
            if (resposta == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Se existir, 200 OK
            return Ok(resposta);
        }

        // DELETE: api/usuarios/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deletar(int id)
        {
            // 1. Pede pro Service deletar
            var deletou = await _usuarioService.DeletarAsync(id);

            // 2. Se não achou, 404
            if (!deletou)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Se deletou, 204
            return NoContent();
        }

        // PUT: api/usuarios/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<UsuarioRespostaDTO>> Atualizar(int id, [FromBody] AtualizarUsuarioDTO dto)
        {
            try
            {
                // 1. Pede pro Service atualizar
                var resposta = await _usuarioService.AtualizarAsync(id, dto);

                // 2. Se não achou, 404
                if (resposta == null)
                    return NotFound(new { mensagem = "Usuário não encontrado." });

                // 3. Se atualizou, 200 OK
                return Ok(resposta);
            }
            catch (InvalidOperationException ex)
            {
                // Regra de negócio (ex: e-mail já existe) -> 409 Conflict
                return Conflict(new { mensagem = ex.Message });
            }
        }

        
        // POST: api/usuarios
        [HttpPost]
        public async Task<ActionResult<UsuarioRespostaDTO>> Criar([FromBody] CriarUsuarioDTO dto)
        {
            try
            {
                // 1. Pede pro Service criar o usuário
                var resposta = await _usuarioService.CriarAsync(dto);

                // 2. Retorna CREATED (201) com o DTO
                return CreatedAtAction(
                    nameof(GetPorId),
                    new { id = resposta.Id },
                    resposta
                );
            }
            catch (InvalidOperationException ex)
            {
                // Regras do Service (ex: e-mails não coincidem, senha não coincide, e-mail repetido)
                // Aqui a gente diferencia:
                // - conflito (email repetido) -> 409
                // - validação/regra de preenchimento -> 400
                var msg = ex.Message;

                if (msg.Contains("Já existe um usuário cadastrado") || msg.Contains("Já existe outro usuário"))
                    return Conflict(new { mensagem = msg });

                return BadRequest(new { mensagem = msg });
            }
        }

        [AllowAnonymous]
        [HttpPost("login")] // POST: api/usuarios/login
        public async Task<ActionResult<LoginRespostaDTO>> Login([FromBody] LoginUsuarioDTO dto)
        {
            // Envia dados para o Service (Service = valida credenciais, gera token, monta a resposta final)
            var resposta = await _usuarioService.LoginAsync(dto);

            // Se resposta do service for Null o email ta errado ou inexistente
            if (resposta == null)
                return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });

            // Se deu tudo certo, devolve 200 OK
            return Ok(resposta);
        }
    }
}