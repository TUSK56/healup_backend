using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlignPatientAddressColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy `patient_addresses` tables sometimes used snake_case for Id/PatientId/etc. Rename to PascalCase
            // to match the rest of the model. (icon_key / address_details stay snake_case — see HealUpDbContext + PatientAddressSnakeIconAndDetails.)
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[patient_addresses]', N'U') IS NULL
                    RETURN;

                IF COL_LENGTH(N'patient_addresses', N'Id') IS NULL AND COL_LENGTH(N'patient_addresses', N'id') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.id', N'Id', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'PatientId') IS NULL AND COL_LENGTH(N'patient_addresses', N'patient_id') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.patient_id', N'PatientId', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'Label') IS NULL AND COL_LENGTH(N'patient_addresses', N'label') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.label', N'Label', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'City') IS NULL AND COL_LENGTH(N'patient_addresses', N'city') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.city', N'City', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'District') IS NULL AND COL_LENGTH(N'patient_addresses', N'district') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.district', N'District', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'Latitude') IS NULL AND COL_LENGTH(N'patient_addresses', N'latitude') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.latitude', N'Latitude', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'Longitude') IS NULL AND COL_LENGTH(N'patient_addresses', N'longitude') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.longitude', N'Longitude', N'COLUMN';

                IF COL_LENGTH(N'patient_addresses', N'CreatedAt') IS NULL AND COL_LENGTH(N'patient_addresses', N'created_at') IS NOT NULL
                    EXEC sp_rename N'patient_addresses.created_at', N'CreatedAt', N'COLUMN';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: reversing column renames could break production data alignment.
        }
    }
}
