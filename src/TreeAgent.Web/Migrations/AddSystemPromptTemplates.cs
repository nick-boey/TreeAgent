using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreeAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemPromptTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultPromptTemplateId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSystemPrompt",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SystemPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    IsGlobal = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemPromptTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemPromptTemplates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DefaultPromptTemplateId",
                table: "Projects",
                column: "DefaultPromptTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemPromptTemplates_ProjectId",
                table: "SystemPromptTemplates",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_SystemPromptTemplates_DefaultPromptTemplateId",
                table: "Projects",
                column: "DefaultPromptTemplateId",
                principalTable: "SystemPromptTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_SystemPromptTemplates_DefaultPromptTemplateId",
                table: "Projects");

            migrationBuilder.DropTable(
                name: "SystemPromptTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DefaultPromptTemplateId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DefaultPromptTemplateId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DefaultSystemPrompt",
                table: "Projects");
        }
    }
}
