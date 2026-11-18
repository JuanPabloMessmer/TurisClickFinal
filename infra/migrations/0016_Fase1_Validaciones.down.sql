-- Migración 0016_Fase1_Validaciones (down), generada con `dotnet ef migrations script --idempotent`.
-- Probada en local (docs/auditoria-validaciones.md, §18). Aplicar con:
--   psql --single-transaction -v ON_ERROR_STOP=1 -f <este archivo>
-- La codificación se fija acá: las expresiones de los índices tienen vocales acentuadas y, con otra codificación
-- de cliente (p. ej. LATIN1), el script se aplicaría sin error pero con índices incorrectos.
SET client_encoding = 'UTF8';

START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    DROP INDEX IF EXISTS uq_companies_legal_document_normalized;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    DROP INDEX IF EXISTS uq_categories_name_key;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    DROP INDEX IF EXISTS uq_companies_name_key;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE experience_categories DROP CONSTRAINT "FK_experience_categories_categories_category_id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE package_categories DROP CONSTRAINT "FK_package_categories_categories_category_id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE tourist_preference_categories DROP CONSTRAINT "FK_tourist_preference_categories_categories_category_id";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE experience_categories ADD CONSTRAINT "FK_experience_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE package_categories ADD CONSTRAINT "FK_package_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE tourist_preference_categories ADD CONSTRAINT "FK_tourist_preference_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE;
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones';
    END IF;
END $EF$;
COMMIT;

