using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TamoJuntoGames.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HelloController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("Esta carroça esta funcionando normalmente! 🥹");
        }
    }
}
