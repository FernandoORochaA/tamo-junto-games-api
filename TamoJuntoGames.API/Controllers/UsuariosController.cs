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
        public ActionResult<IEnumerable<UsuarioRespostaDTO>> GetTodos()
        {
            // Converte cada Usuario da lista para UsuarioRespostaDTO
            var resposta = Usuarios
                .Select(ParaResposta)
                .ToList();

            return Ok(resposta);
        }

        // GET: api/usuarios/{id}
        [HttpGet("{id:int}")]
        public ActionResult<UsuarioRespostaDTO> GetPorId(int id)
        {
            var usuario = Usuarios.FirstOrDefault(u => u.Id == id);

            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // Converte o usuário completo para o DTO de resposta
            var resposta = ParaResposta(usuario);

            return Ok(resposta);
        }

        // DELETE: api/usuarios/{id}
        [HttpDelete("{id:int}")]
        public IActionResult Deletar(int id)
        {
            // 1. Localizar o usuario na lista
            var usuario = Usuarios.FirstOrDefault(u => u.Id == id);

            // 2. Caso não localize. 404 (não encontrado)
            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Se achou, remove da lista
            Usuarios.Remove(usuario);

            // 4. Devolve 204 (sucesso, porém sem corpo na resposta)
            return NoContent();
        }

        // PUT: api/usuarios/{id}
        [HttpPut("{id:int}")]
        public ActionResult<UsuarioRespostaDTO> Atualizar(int id, [FromBody] AtualizarUsuarioDTO dto)
        {
            // 1. Procurar o usuário na lista
            var usuario = Usuarios.FirstOrDefault(u => u.Id == id);

            // 2. Se não achar, devolver 404
            if (usuario == null)
                return NotFound(new { mensagem = "Usuário não encontrado." });

            // 3. Verificar se o novo e-mail já não está em uso por OUTRO usuário
            var emailJaExiste = Usuarios.Any(u => u.Email == dto.Email && u.Id != id);
            if (emailJaExiste)
                return Conflict(new { mensagem = "Já existe outro usuário com esse e-mail." });

            // 4. Atualizar os dados do usuário encontrado
            usuario.NomeCompleto = dto.NomeCompleto;
            usuario.Apelido = dto.Apelido;
            usuario.Email = dto.Email;

            // 5. Montar DTO de resposta (sem senha ainda)
            var resposta = ParaResposta(usuario);

            // 6. Devolver 200 OK com os dados atualizados
            return Ok(resposta);
        }

        // POST: api/usuarios
        [HttpPost]
        public ActionResult<UsuarioRespostaDTO> Criar([FromBody] CriarUsuarioDTO dto)
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

            // 6. Montar DTO de resposta (sem senha)
            var resposta = ParaResposta(novoUsuario);

            // 7. Retornar CREATED (201) com o DTO
            return CreatedAtAction(
                nameof(GetPorId),
                new { id = novoUsuario.Id },
                resposta
            );
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