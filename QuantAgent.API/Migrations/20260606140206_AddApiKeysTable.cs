using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantAgent.API.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeysTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    deporte = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    ultimo_uso = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    exitos = table.Column<int>(type: "integer", nullable: false),
                    fallos = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_deporte",
                table: "api_keys",
                column: "deporte");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_estado",
                table: "api_keys",
                column: "estado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");
        }
    }
}
