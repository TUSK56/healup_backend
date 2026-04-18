IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [pharmacies] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(255) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [LicenseNumber] nvarchar(100) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_pharmacies] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [users] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(255) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Role] nvarchar(32) NOT NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [notifications] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NULL,
        [PharmacyId] int NULL,
        [Type] nvarchar(64) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [IsRead] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_notifications_pharmacies_PharmacyId] FOREIGN KEY ([PharmacyId]) REFERENCES [pharmacies] ([Id]),
        CONSTRAINT [FK_notifications_users_UserId] FOREIGN KEY ([UserId]) REFERENCES [users] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [requests] (
        [Id] int NOT NULL IDENTITY,
        [PatientId] int NOT NULL,
        [PrescriptionUrl] nvarchar(max) NULL,
        [Status] nvarchar(32) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_requests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_requests_users_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [orders] (
        [Id] int NOT NULL IDENTITY,
        [PatientId] int NOT NULL,
        [PharmacyId] int NOT NULL,
        [RequestId] int NOT NULL,
        [Delivery] bit NOT NULL,
        [DeliveryFee] decimal(18,2) NOT NULL,
        [TotalPrice] decimal(18,2) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_orders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_orders_pharmacies_PharmacyId] FOREIGN KEY ([PharmacyId]) REFERENCES [pharmacies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_orders_requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [requests] ([Id]),
        CONSTRAINT [FK_orders_users_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [pharmacy_responses] (
        [Id] int NOT NULL IDENTITY,
        [PharmacyId] int NOT NULL,
        [RequestId] int NOT NULL,
        [DeliveryFee] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_pharmacy_responses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_pharmacy_responses_pharmacies_PharmacyId] FOREIGN KEY ([PharmacyId]) REFERENCES [pharmacies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_pharmacy_responses_requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [requests] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [request_medicines] (
        [Id] int NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [MedicineName] nvarchar(255) NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_request_medicines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_request_medicines_requests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [requests] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [order_items] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [MedicineName] nvarchar(255) NOT NULL,
        [Quantity] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_order_items] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_order_items_orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE TABLE [response_medicines] (
        [Id] int NOT NULL IDENTITY,
        [ResponseId] int NOT NULL,
        [MedicineName] nvarchar(255) NOT NULL,
        [Available] bit NOT NULL,
        [QuantityAvailable] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_response_medicines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_response_medicines_pharmacy_responses_ResponseId] FOREIGN KEY ([ResponseId]) REFERENCES [pharmacy_responses] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_notifications_PharmacyId] ON [notifications] ([PharmacyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_notifications_UserId] ON [notifications] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_order_items_OrderId] ON [order_items] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_orders_PatientId] ON [orders] ([PatientId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_orders_PharmacyId] ON [orders] ([PharmacyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_orders_RequestId] ON [orders] ([RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_pharmacies_Email] ON [pharmacies] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_pharmacy_responses_PharmacyId] ON [pharmacy_responses] ([PharmacyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_pharmacy_responses_RequestId] ON [pharmacy_responses] ([RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_request_medicines_RequestId] ON [request_medicines] ([RequestId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_requests_PatientId] ON [requests] ([PatientId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_response_medicines_ResponseId] ON [response_medicines] ([ResponseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_users_Email] ON [users] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260313031157_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260313031157_InitialCreate', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [notifications] DROP CONSTRAINT [FK_notifications_users_UserId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [orders] DROP CONSTRAINT [FK_orders_users_PatientId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [requests] DROP CONSTRAINT [FK_requests_users_PatientId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    DROP TABLE [users];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    EXEC sp_rename N'[notifications].[UserId]', N'PatientId', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    EXEC sp_rename N'[notifications].[IX_notifications_UserId]', N'IX_notifications_PatientId', N'INDEX';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    CREATE TABLE [admins] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(255) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_admins] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    CREATE TABLE [patients] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(255) NOT NULL,
        [Email] nvarchar(255) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_patients] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_admins_Email] ON [admins] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_patients_Email] ON [patients] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [notifications] ADD CONSTRAINT [FK_notifications_patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [orders] ADD CONSTRAINT [FK_orders_patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    ALTER TABLE [requests] ADD CONSTRAINT [FK_requests_patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [patients] ([Id]) ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260409164141_SplitIdentityTablesAndOtpTesting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260409164141_SplitIdentityTablesAndOtpTesting', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260415001917_NotificationsTargetRoute'
)
BEGIN
    ALTER TABLE [notifications] ADD [TargetRoute] nvarchar(512) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260415001917_NotificationsTargetRoute'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260415001917_NotificationsTargetRoute', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417122545_PharmacyProfileFields'
)
BEGIN
    IF COL_LENGTH('pharmacies', 'AddressDetails') IS NULL
        ALTER TABLE [pharmacies] ADD [AddressDetails] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417122545_PharmacyProfileFields'
)
BEGIN
    IF COL_LENGTH('pharmacies', 'City') IS NULL
        ALTER TABLE [pharmacies] ADD [City] nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417122545_PharmacyProfileFields'
)
BEGIN
    IF COL_LENGTH('pharmacies', 'District') IS NULL
        ALTER TABLE [pharmacies] ADD [District] nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417122545_PharmacyProfileFields'
)
BEGIN
    IF COL_LENGTH('pharmacies', 'ResponsiblePharmacistName') IS NULL
        ALTER TABLE [pharmacies] ADD [ResponsiblePharmacistName] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417122545_PharmacyProfileFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417122545_PharmacyProfileFields', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417134305_RequestEstimatedTotal'
)
BEGIN
    ALTER TABLE [requests] ADD [EstimatedTotal] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417134305_RequestEstimatedTotal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417134305_RequestEstimatedTotal', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417150556_PatientProfileAndAddresses'
)
BEGIN
    IF COL_LENGTH('patients', 'AvatarUrl') IS NULL
        ALTER TABLE [patients] ADD [AvatarUrl] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417150556_PatientProfileAndAddresses'
)
BEGIN
    IF COL_LENGTH('patients', 'DateOfBirth') IS NULL
        ALTER TABLE [patients] ADD [DateOfBirth] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417150556_PatientProfileAndAddresses'
)
BEGIN
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
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417150556_PatientProfileAndAddresses'
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'IX_patient_addresses_PatientId'
          AND object_id = OBJECT_ID(N'[patient_addresses]')
    )
    BEGIN
        CREATE INDEX [IX_patient_addresses_PatientId] ON [patient_addresses] ([PatientId]);
    END
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417150556_PatientProfileAndAddresses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417150556_PatientProfileAndAddresses', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417220512_AlignPatientAddressColumns'
)
BEGIN
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
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417220512_AlignPatientAddressColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417220512_AlignPatientAddressColumns', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417223624_PatientAddressSnakeIconAndDetails'
)
BEGIN
    IF OBJECT_ID(N'[dbo].[patient_addresses]', N'U') IS NULL
        RETURN;
    IF COL_LENGTH(N'patient_addresses', N'icon_key') IS NULL
        AND COL_LENGTH(N'patient_addresses', N'IconKey') IS NOT NULL
        EXEC sp_rename N'patient_addresses.IconKey', N'icon_key', N'COLUMN';
    IF COL_LENGTH(N'patient_addresses', N'address_details') IS NULL
        AND COL_LENGTH(N'patient_addresses', N'AddressDetails') IS NOT NULL
        EXEC sp_rename N'patient_addresses.AddressDetails', N'address_details', N'COLUMN';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417223624_PatientAddressSnakeIconAndDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417223624_PatientAddressSnakeIconAndDetails', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417225711_PatientAddressLegacyColumnFix'
)
BEGIN
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
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260417225711_PatientAddressLegacyColumnFix'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260417225711_PatientAddressLegacyColumnFix', N'8.0.0');
END;
GO

COMMIT;
GO

