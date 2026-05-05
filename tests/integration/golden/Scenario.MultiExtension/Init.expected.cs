using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Scenario.MultiExtension.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });
            migrationBuilder.Sql("CREATE MATERIALIZED VIEW \"ProductCount\" AS SELECT COUNT(*) AS \"Count\" FROM \"Products\";");
            migrationBuilder.Sql("CREATE INDEX \"ix_gin_Products_Name\" ON \"Products\" USING gin (to_tsvector('english', \"Name\"));");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS \"ProductCount\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"ix_gin_Products_Name\";");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
