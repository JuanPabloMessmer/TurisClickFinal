START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT "FK_reservation_items_provider_flight_departures_provider_fligh~";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT "FK_reservation_items_provider_flights_provider_flight_id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP TABLE provider_flight_departures;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP TABLE reservation_passengers;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP TABLE provider_flights;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP INDEX ix_reservation_items_issuance;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP INDEX ix_reservation_items_provider_flight_departure;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DROP INDEX "IX_reservation_items_provider_flight_id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT ck_reservation_items_issuance;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT ck_reservation_items_issued_tickets;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT ck_reservation_items_product_shape;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN booking_reference;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN issuance_note;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN issuance_status;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN issued_at;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN issued_by;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN provider_flight_departure_id;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN provider_flight_id;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP COLUMN ticket_numbers;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies DROP COLUMN can_sell_flights;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies DROP COLUMN flight_seller_authorized_at;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies DROP COLUMN flight_seller_authorized_by;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies DROP COLUMN flight_seller_registration;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT ck_reservation_items_product_shape CHECK ((product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL));
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador';
    END IF;
END $EF$;
COMMIT;

