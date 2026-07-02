using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Photino.Okf_Todo.Data;

#nullable disable

namespace Photino.Okf_Todo.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260702191500_AddMarkdownEditorPreference")]
    public partial class AddMarkdownEditorPreference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyMarkdown",
                table: "Issues",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EditorMode",
                table: "Issues",
                type: "TEXT",
                nullable: false,
                defaultValue: "html");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyMarkdown",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "EditorMode",
                table: "Issues");
        }
    }
}
