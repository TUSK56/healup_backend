using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PatientAddressLegacyColumnFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[patient_addresses]', N'U') IS NULL
                    RETURN;

                IF COL_LENGTH(N'patient_addresses', N'icon_key') IS NULL        
                    AND COL_LENGTH(N'patient_addresses', N'IconKey') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.IconKey', N'icon_key', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'icon_key') IS NULL        
                    AND COL_LENGTH(N'patient_addresses', N'Kind') IS NOT NULL   
                    EXEC sp_rename N'patient_addresses.Kind', N'icon_key', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'address_details') IS NULL 
                BEGIN
                    IF COL_LENGTH(N'patient_addresses', N'AddressDetails') IS NOT NULL
                        EXEC sp_rename N'patient_addresses.AddressDetails', N'address_details', N'COLUMN';
                    ELSE IF COL_LENGTH(N'patient_addresses', N'Street') IS NOT NULL
                        EXEC sp_rename N'patient_addresses.Street', N'address_details', N'COLUMN';
                    ELSE IF COL_LENGTH(N'patient_addresses', N'FormattedAddress') IS NOT NULL
                        EXEC sp_rename N'patient_addresses.FormattedAddress', N'address_details', N'COLUMN';
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left empty to avoid renaming columns back in environments already aligned to the EF model.
        }
    }
}
