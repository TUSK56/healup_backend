using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PharmacyProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'AddressDetails') IS NULL
                    ALTER TABLE [pharmacies] ADD [AddressDetails] nvarchar(500) NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'City') IS NULL
                    ALTER TABLE [pharmacies] ADD [City] nvarchar(120) NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'District') IS NULL
                    ALTER TABLE [pharmacies] ADD [District] nvarchar(120) NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'ResponsiblePharmacistName') IS NULL
                    ALTER TABLE [pharmacies] ADD [ResponsiblePharmacistName] nvarchar(255) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'AddressDetails') IS NOT NULL
                    ALTER TABLE [pharmacies] DROP COLUMN [AddressDetails];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'City') IS NOT NULL
                    ALTER TABLE [pharmacies] DROP COLUMN [City];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'District') IS NOT NULL
                    ALTER TABLE [pharmacies] DROP COLUMN [District];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('pharmacies', 'ResponsiblePharmacistName') IS NOT NULL
                    ALTER TABLE [pharmacies] DROP COLUMN [ResponsiblePharmacistName];
                """);
        }
    }
}
