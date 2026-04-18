using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PatientAddressSnakeIconAndDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Older DBs (or pre-mapping installs) may still have [IconKey] / [AddressDetails]. EF maps to icon_key / address_details.
            // Rename Pascal → snake only when needed. Fresh installs already get icon_key / address_details from PatientProfileAndAddresses.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[patient_addresses]', N'U') IS NULL
                    RETURN;

                IF COL_LENGTH(N'patient_addresses', N'icon_key') IS NULL
                    AND COL_LENGTH(N'patient_addresses', N'IconKey') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.IconKey', N'icon_key', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'address_details') IS NULL
                    AND COL_LENGTH(N'patient_addresses', N'AddressDetails') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.AddressDetails', N'address_details', N'COLUMN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: reversing renames risks breaking environments already aligned to snake_case.
        }
    }
}
