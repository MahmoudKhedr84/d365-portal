using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D365Portal.Infrastructure.Migrations.Postgres
{
    /// <summary>Tenant option: the D365 environment exposes Model / Type / Year (InventDimension1-3) on its entities.</summary>
    public partial class ExtendedProductDimensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: the column may already exist (it was added ahead of this deploy).
            migrationBuilder.Sql(
                "ALTER TABLE d365_connections ADD COLUMN IF NOT EXISTS \"UseExtendedProductDimensions\" boolean NOT NULL DEFAULT FALSE;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UseExtendedProductDimensions", table: "d365_connections");
        }
    }
}
