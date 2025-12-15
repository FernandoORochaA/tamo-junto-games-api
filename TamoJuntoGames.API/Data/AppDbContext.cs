using Microsoft.EntityFrameworkCore;
using TamoJuntoGames.API.Models;

namespace TamoJuntoGames.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
    }
}
