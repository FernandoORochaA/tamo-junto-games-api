using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TamoJuntoGames.API.Migrations
{
    /// <inheritdoc />
    public partial class EmailNormalizadoUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailNormalizado",
                table: "Usuarios",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE \"Usuarios\" " +
                "SET \"Email\" = trim(\"Email\"), " +
                "\"EmailNormalizado\" = upper(trim(\"Email\"));");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_EmailNormalizado",
                table: "Usuarios",
                column: "EmailNormalizado",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Usuarios_EmailNormalizado",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "EmailNormalizado",
                table: "Usuarios");
        }
    }
}
