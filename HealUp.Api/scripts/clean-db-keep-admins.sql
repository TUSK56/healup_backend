-- HealUp: remove all application data except [admins].
-- Keeps admin accounts (name, email, password hash) and the schema / migration history.
--
-- HOW TO RUN
--   • MonsterASP: open "Run T-SQL", connect to your HealUp database, paste this entire script.
--   • Local:      sqlcmd -S YOUR_SERVER -d YOUR_DATABASE -i clean-db-keep-admins.sql
--
-- WARNING: Irreversible. Deletes patients, pharmacies, orders, requests, notifications, etc.
--          Verify you are connected to the correct database before executing.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- Child tables first (FK-safe order; same pattern as HealUp.DataExport).
DELETE FROM [order_items];
DELETE FROM [orders];
DELETE FROM [response_medicines];
DELETE FROM [pharmacy_responses];
DELETE FROM [pharmacy_declined_requests];
DELETE FROM [request_medicines];
DELETE FROM [requests];
DELETE FROM [notifications];
DELETE FROM [patient_addresses];
DELETE FROM [patients];
DELETE FROM [pharmacies];

-- [admins] is intentionally NOT deleted.

COMMIT TRANSACTION;

-- Row counts after cleanup (admins should be > 0 if seeded; everything else should be 0).
SELECT 'admins' AS [table], COUNT(*) AS [rows] FROM [admins]
UNION ALL SELECT 'patients', COUNT(*) FROM [patients]
UNION ALL SELECT 'pharmacies', COUNT(*) FROM [pharmacies]
UNION ALL SELECT 'requests', COUNT(*) FROM [requests]
UNION ALL SELECT 'orders', COUNT(*) FROM [orders]
UNION ALL SELECT 'notifications', COUNT(*) FROM [notifications]
ORDER BY [table];
