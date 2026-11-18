using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurisClick.Api.Infrastructure.Database.Migrations
{
    /// <summary>
    /// Fase 1 de validaciones (docs/auditoria-validaciones.md, §12):
    ///
    /// 1. Verificación previa: si ya hay empresas o categorías con el mismo nombre normalizado, o empresas con el
    ///    mismo NIT normalizado, la migración se detiene con un mensaje en español y no aplica nada (corre en una
    ///    transacción). Esos datos se corrigen a mano con el procedimiento de §14; la migración no los toca.
    /// 2. FK de las categorías en experiencias, paquetes y preferencias de turistas: de CASCADE a RESTRICT
    ///    (borrar una categoría en uso quedó prohibido, D-11).
    /// 3. Índices únicos por expresión sobre el nombre normalizado de empresas y categorías y sobre el NIT
    ///    normalizado. EF Core no modela índices por expresión: se crean con SQL. Las expresiones son copia
    ///    literal de Shared/Validation/NameKeySql (un test lo verifica) y no deben cambiar aunque el código cambie.
    ///
    /// No agrega CHECK de formato: un CHECK NOT VALID se evalúa en cualquier UPDATE posterior de filas viejas, y
    /// aprobar o suspender una empresa con un NIT viejo inválido empezaría a fallar. Van después de corregir los
    /// datos existentes.
    /// </summary>
    public partial class _0016_Fase1_Validaciones : Migration
    {
        /// <summary>K1: nombre sin mayúsculas, tildes ni espacios repetidos (copia de NameKeySql.NameKey("name")).</summary>
        public const string NameKeyExpression = "translate(lower(regexp_replace(btrim(name), '\\s+', ' ', 'g')), 'ÁÀÂÄÃÉÈÊËÍÌÎÏÓÒÔÖÕÚÙÛÜÑáàâäãéèêëíìîïóòôöõúùûü', 'aaaaaeeeeiiiiooooouuuuñaaaaaeeeeiiiiooooouuuu')";

        /// <summary>NIT sin espacios, puntos ni guiones (copia de NameKeySql.NormalizedNit("legal_document")).</summary>
        public const string NormalizedNitExpression = "regexp_replace(legal_document, '[\\s.\\-]', '', 'g')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE
                    duplicados text;
                BEGIN
                    SELECT string_agg(nombres, '; ') INTO duplicados FROM (
                        SELECT string_agg(name, ' / ') AS nombres FROM companies
                        GROUP BY {{NameKeyExpression}} HAVING count(*) > 1) d;
                    IF duplicados IS NOT NULL THEN
                        RAISE EXCEPTION 'Migración 0016 detenida: hay empresas con el mismo nombre normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
                    END IF;

                    SELECT string_agg(nombres, '; ') INTO duplicados FROM (
                        SELECT string_agg(name, ' / ') AS nombres FROM categories
                        GROUP BY {{NameKeyExpression}} HAVING count(*) > 1) d;
                    IF duplicados IS NOT NULL THEN
                        RAISE EXCEPTION 'Migración 0016 detenida: hay categorías con el mismo nombre normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
                    END IF;

                    SELECT string_agg(nombres, '; ') INTO duplicados FROM (
                        SELECT string_agg(name, ' / ') AS nombres FROM companies
                        GROUP BY {{NormalizedNitExpression}} HAVING count(*) > 1) d;
                    IF duplicados IS NOT NULL THEN
                        RAISE EXCEPTION 'Migración 0016 detenida: hay empresas con el mismo NIT normalizado (%). Corregilas antes de aplicarla (docs/auditoria-validaciones.md, §14).', duplicados;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_experience_categories_categories_category_id",
                table: "experience_categories");

            migrationBuilder.DropForeignKey(
                name: "FK_package_categories_categories_category_id",
                table: "package_categories");

            migrationBuilder.DropForeignKey(
                name: "FK_tourist_preference_categories_categories_category_id",
                table: "tourist_preference_categories");

            migrationBuilder.AddForeignKey(
                name: "FK_experience_categories_categories_category_id",
                table: "experience_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_package_categories_categories_category_id",
                table: "package_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tourist_preference_categories_categories_category_id",
                table: "tourist_preference_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql($"CREATE UNIQUE INDEX uq_companies_name_key ON companies (({NameKeyExpression}));");
            migrationBuilder.Sql($"CREATE UNIQUE INDEX uq_categories_name_key ON categories (({NameKeyExpression}));");
            migrationBuilder.Sql($"CREATE UNIQUE INDEX uq_companies_legal_document_normalized ON companies (({NormalizedNitExpression}));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_companies_legal_document_normalized;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_categories_name_key;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_companies_name_key;");

            migrationBuilder.DropForeignKey(
                name: "FK_experience_categories_categories_category_id",
                table: "experience_categories");

            migrationBuilder.DropForeignKey(
                name: "FK_package_categories_categories_category_id",
                table: "package_categories");

            migrationBuilder.DropForeignKey(
                name: "FK_tourist_preference_categories_categories_category_id",
                table: "tourist_preference_categories");

            migrationBuilder.AddForeignKey(
                name: "FK_experience_categories_categories_category_id",
                table: "experience_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_package_categories_categories_category_id",
                table: "package_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_tourist_preference_categories_categories_category_id",
                table: "tourist_preference_categories",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
