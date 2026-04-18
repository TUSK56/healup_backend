-- Run against your HealUp SQL Server database when the API is stopped (or use EF migrations).
IF COL_LENGTH('dbo.orders', 'PreparingAt') IS NULL
  ALTER TABLE dbo.orders ADD PreparingAt datetime2 NULL;

IF COL_LENGTH('dbo.orders', 'PaymentMethod') IS NULL
  ALTER TABLE dbo.orders ADD PaymentMethod nvarchar(64) NULL;

IF COL_LENGTH('dbo.orders', 'DeliveryAddressSnapshot') IS NULL
  ALTER TABLE dbo.orders ADD DeliveryAddressSnapshot nvarchar(500) NULL;

IF OBJECT_ID('dbo.pharmacy_declined_requests', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.pharmacy_declined_requests (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PharmacyId int NOT NULL,
    RequestId int NOT NULL,
    CreatedAt datetime2 NOT NULL,
    CONSTRAINT FK_pdr_pharmacies FOREIGN KEY (PharmacyId) REFERENCES dbo.pharmacies(Id) ON DELETE CASCADE,
    CONSTRAINT FK_pdr_requests FOREIGN KEY (RequestId) REFERENCES dbo.requests(Id) ON DELETE CASCADE
  );
  CREATE UNIQUE INDEX IX_pharmacy_declined_requests_PharmacyId_RequestId
    ON dbo.pharmacy_declined_requests(PharmacyId, RequestId);
END
