using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OkfTodo.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskOwnerAndResponsible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Responsible",
                table: "TaskItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Owner",
                table: "TaskItems");

            migrationBuilder.DropColumn(
                name: "Responsible",
                table: "TaskItems");
        }
    }
}
