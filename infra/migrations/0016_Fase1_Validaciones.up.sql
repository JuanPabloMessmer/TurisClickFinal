-- Migración 0016_Fase1_Validaciones (up), generada con `dotnet ef migrations script --idempotent`.
-- Probada en local (docs/auditoria-validaciones.md, §18). Aplicar con:
--   psql --single-transaction -v ON_ERROR_STOP=1 -f <este archivo>
-- La codificación se fija acá: las expresiones de los índices tienen vocales acentuadas y, con otra codificación
-- de cliente (p. ej. LATIN1), el script se aplicaría sin error pero con índices incorrectos.
SET client_encoding = 'UTF8';

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    DO $$
    DECLARE
        duplicados text;
    BEGIN
        SELECT string_agg(nombres, '; ') INTO duplicados FROM (
            SELECT string_agg(name, ' / ') AS nombres FROM companies
            GROUP BY translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu') HAVING count(*) > 1) d;
        IF duplicados IS NOT NULL THEN
            RAISE EXCEPTION 'Migración 0016 detenida: hay empresas con el mismo nombre normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
        END IF;

        SELECT string_agg(nombres, '; ') INTO duplicados FROM (
            SELECT string_agg(name, ' / ') AS nombres FROM categories
            GROUP BY translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu') HAVING count(*) > 1) d;
        IF duplicados IS NOT NULL THEN
            RAISE EXCEPTION 'Migración 0016 detenida: hay categorías con el mismo nombre normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
        END IF;

        SELECT string_agg(nombres, '; ') INTO duplicados FROM (
            SELECT string_agg(name, ' / ') AS nombres FROM companies
            GROUP BY regexp_replace(legal_document, '[\s.\-]', '', 'g') HAVING count(*) > 1) d;
        IF duplicados IS NOT NULL THEN
            RAISE EXCEPTION 'Migración 0016 detenida: hay empresas con el mismo NIT normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE experience_categories DROP CONSTRAINT "FK_experience_categories_categories_category_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE package_categories DROP CONSTRAINT "FK_package_categories_categories_category_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE tourist_preference_categories DROP CONSTRAINT "FK_tourist_preference_categories_categories_category_id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE experience_categories ADD CONSTRAINT "FK_experience_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE package_categories ADD CONSTRAINT "FK_package_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    ALTER TABLE tourist_preference_categories ADD CONSTRAINT "FK_tourist_preference_categories_categories_category_id" FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    CREATE UNIQUE INDEX uq_companies_name_key ON companies ((translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    CREATE UNIQUE INDEX uq_categories_name_key ON categories ((translate(lower(regexp_replace(btrim(name), '\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    CREATE UNIQUE INDEX uq_companies_legal_document_normalized ON companies ((regexp_replace(legal_document, '[\s.\-]', '', 'g')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118003840_0016_Fase1_Validaciones') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261118003840_0016_Fase1_Validaciones', '10.0.10');
    END IF;
END $EF$;
COMMIT;

