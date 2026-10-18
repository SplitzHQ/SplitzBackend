using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitzBackend.src.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LLMSettings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ProtectedApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_LLMSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptRecognitions",
                columns: table => new
                {
                    ReceiptRecognitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ImageKeys = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UsedCustomProvider = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RawResponse = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Merchant = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    TransactionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Location = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptRecognitions", x => x.ReceiptRecognitionId);
                    table.ForeignKey(
                        name: "FK_ReceiptRecognitions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReceiptRecognitions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRecognitions_TransactionId",
                table: "ReceiptRecognitions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptRecognitions_UserId_CreateTime",
                table: "ReceiptRecognitions",
                columns: new[] { "UserId", "CreateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LLMSettings");

            migrationBuilder.DropTable(
                name: "ReceiptRecognitions");
        }
    }
}
