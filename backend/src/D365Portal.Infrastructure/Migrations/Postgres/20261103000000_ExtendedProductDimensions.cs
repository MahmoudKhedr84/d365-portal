using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace D365Portal.Infrastructure.Migrations.Postgres
{
    /// <summary>Tenant option: the D365 environment exposes Model / Type / Year (InventDimension1-3) on its entities.</summary>
    public partial class ExtendedProductDimensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseExtendedProductDimensions",
                table: "d365_connections",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UseExtendedProductDimensions", table: "d365_connections");
        }
    }
}
