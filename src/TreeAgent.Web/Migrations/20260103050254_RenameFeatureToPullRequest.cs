using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreeAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class RenameFeatureToPullRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_Features_FeatureId",
                table: "Agents");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.RenameColumn(
                name: "FeatureId",
                table: "Agents",
                newName: "PullRequestId");

            migrationBuilder.RenameIndex(
                name: "IX_Agents_FeatureId",
                table: "Agents",
                newName: "IX_Agents_PullRequestId");

            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BranchName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubPRNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    WorktreePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PullRequests_PullRequests_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_ParentId",
                table: "PullRequests",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_ProjectId",
                table: "PullRequests",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_PullRequests_PullRequestId",
                table: "Agents",
                column: "PullRequestId",
                principalTable: "PullRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Agents_PullRequests_PullRequestId",
                table: "Agents");

            migrationBuilder.DropTable(
                name: "PullRequests");

            migrationBuilder.RenameColumn(
                name: "PullRequestId",
                table: "Agents",
                newName: "FeatureId");

            migrationBuilder.RenameIndex(
                name: "IX_Agents_PullRequestId",
                table: "Agents",
                newName: "IX_Agents_FeatureId");

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    GitHubPRNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WorktreePath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Features_Features_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Features_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Features_ParentId",
                table: "Features",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_ProjectId",
                table: "Features",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Agents_Features_FeatureId",
                table: "Agents",
                column: "FeatureId",
                principalTable: "Features",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
