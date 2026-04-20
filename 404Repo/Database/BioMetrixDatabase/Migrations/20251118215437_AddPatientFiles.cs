using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BioMetrixDatabase.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PatientFiles_Users_UploadedBy",
                table: "PatientFiles");

            migrationBuilder.DropIndex(
                name: "IX_PatientFiles_UploadedBy",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "PatientFiles");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "PatientFiles",
                newName: "FileSize");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "PatientFiles",
                newName: "UpdatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "UploadedBy",
                table: "PatientFiles",
                type: "TEXT",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PatientFiles",
                type: "TEXT",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "PatientFiles",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "PatientFiles",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "FileSize",
                table: "PatientFiles",
                newName: "IsDeleted");

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedBy",
                table: "PatientFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PatientFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "PatientFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "PatientFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "PatientFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "PatientFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_PatientFiles_UploadedBy",
                table: "PatientFiles",
                column: "UploadedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_PatientFiles_Users_UploadedBy",
                table: "PatientFiles",
                column: "UploadedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
