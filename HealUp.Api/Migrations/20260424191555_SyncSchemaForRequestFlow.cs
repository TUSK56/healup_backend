using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncSchemaForRequestFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_requests_PatientId",
                table: "requests");

            migrationBuilder.DropIndex(
                name: "IX_pharmacy_responses_PharmacyId",
                table: "pharmacy_responses");

            migrationBuilder.DropIndex(
                name: "IX_pharmacy_responses_RequestId",
                table: "pharmacy_responses");

            migrationBuilder.DropIndex(
                name: "IX_orders_PatientId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_PharmacyId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_RequestId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_notifications_PatientId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_PharmacyId",
                table: "notifications");

            migrationBuilder.AddColumn<int>(
                name: "NotifiedPharmacyCount",
                table: "requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CouponPercent",
                table: "orders",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddressSnapshot",
                table: "orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "orders",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreparingAt",
                table: "orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AdminId",
                table: "notifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pharmacy_declined_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PharmacyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pharmacy_declined_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pharmacy_declined_requests_pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pharmacy_declined_requests_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_requests_PatientId_CreatedAt",
                table: "requests",
                columns: new[] { "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_requests_Status_ExpiresAt",
                table: "requests",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_responses_PharmacyId_RequestId",
                table: "pharmacy_responses",
                columns: new[] { "PharmacyId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_responses_RequestId_CreatedAt",
                table: "pharmacy_responses",
                columns: new[] { "RequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_PatientId_CreatedAt",
                table: "orders",
                columns: new[] { "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_PharmacyId_CreatedAt",
                table: "orders",
                columns: new[] { "PharmacyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_RequestId_CreatedAt",
                table: "orders",
                columns: new[] { "RequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status_CreatedAt",
                table: "orders",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_AdminId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "AdminId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PatientId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "PatientId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PharmacyId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "PharmacyId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_declined_requests_PharmacyId_RequestId",
                table: "pharmacy_declined_requests",
                columns: new[] { "PharmacyId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_declined_requests_RequestId",
                table: "pharmacy_declined_requests",
                column: "RequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_notifications_admins_AdminId",
                table: "notifications",
                column: "AdminId",
                principalTable: "admins",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_notifications_admins_AdminId",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "pharmacy_declined_requests");

            migrationBuilder.DropIndex(
                name: "IX_requests_PatientId_CreatedAt",
                table: "requests");

            migrationBuilder.DropIndex(
                name: "IX_requests_Status_ExpiresAt",
                table: "requests");

            migrationBuilder.DropIndex(
                name: "IX_pharmacy_responses_PharmacyId_RequestId",
                table: "pharmacy_responses");

            migrationBuilder.DropIndex(
                name: "IX_pharmacy_responses_RequestId_CreatedAt",
                table: "pharmacy_responses");

            migrationBuilder.DropIndex(
                name: "IX_orders_PatientId_CreatedAt",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_PharmacyId_CreatedAt",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_RequestId_CreatedAt",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_Status_CreatedAt",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_notifications_AdminId_IsRead_CreatedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_PatientId_IsRead_CreatedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_PharmacyId_IsRead_CreatedAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "NotifiedPharmacyCount",
                table: "requests");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CouponPercent",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressSnapshot",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PreparingAt",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "AdminId",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_requests_PatientId",
                table: "requests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_responses_PharmacyId",
                table: "pharmacy_responses",
                column: "PharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_responses_RequestId",
                table: "pharmacy_responses",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_PatientId",
                table: "orders",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_PharmacyId",
                table: "orders",
                column: "PharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_RequestId",
                table: "orders",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PatientId",
                table: "notifications",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_PharmacyId",
                table: "notifications",
                column: "PharmacyId");
        }
    }
}
