using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FabricTenantManager.Migrations
{
    /// <inheritdoc />
    public partial class Latest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApplicationObjectId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServicePrincipalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkspaceDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkspaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkspaceUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SolutionName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataFileUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SolutionDeployed = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
