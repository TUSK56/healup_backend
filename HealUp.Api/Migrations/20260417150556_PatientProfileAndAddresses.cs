using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealUp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PatientProfileAndAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH('patients', 'AvatarUrl') IS NULL
                    ALTER TABLE [patients] ADD [AvatarUrl] nvarchar(1000) NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('patients', 'DateOfBirth') IS NULL
                    ALTER TABLE [patients] ADD [DateOfBirth] datetime2 NULL;
                """);

            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[patient_addresses]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [patient_addresses] (
                        [Id] int NOT NULL IDENTITY,
                        [PatientId] int NOT NULL,
                        [Label] nvarchar(80) NOT NULL,
                        [icon_key] nvarchar(32) NOT NULL,
                        [City] nvarchar(120) NULL,
                        [District] nvarchar(120) NULL,
                        [address_details] nvarchar(500) NULL,
                        [Latitude] float NULL,
                        [Longitude] float NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        CONSTRAINT [PK_patient_addresses] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_patient_addresses_patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients] ([Id]) ON DELETE CASCADE
                    );
                END
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_patient_addresses_PatientId'
                      AND object_id = OBJECT_ID(N'[patient_addresses]')
                )
                BEGIN
                    CREATE INDEX [IX_patient_addresses_PatientId] ON [patient_addresses] ([PatientId]);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[patient_addresses]', N'U') IS NOT NULL
                    DROP TABLE [patient_addresses];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('patients', 'AvatarUrl') IS NOT NULL
                    ALTER TABLE [patients] DROP COLUMN [AvatarUrl];
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('patients', 'DateOfBirth') IS NOT NULL
                    ALTER TABLE [patients] DROP COLUMN [DateOfBirth];
                """);
        }
    }
}
