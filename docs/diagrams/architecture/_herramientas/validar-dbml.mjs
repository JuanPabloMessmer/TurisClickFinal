// Valida la sintaxis del DBML con el parser oficial de dbdiagram.io (@dbml/core).
import fs from 'node:fs';
import { createRequire } from 'node:module';
const { Parser } = createRequire(import.meta.url)('@dbml/core');
const file = process.argv[2];
try {
  const db = new Parser().parse(fs.readFileSync(file, 'utf8'), 'dbml');
  const s = db.schemas[0];
  console.log(`DBML válido: ${s.tables.length} tablas, ${s.refs.length} relaciones, ${s.tableGroups.length} grupos`);
} catch (e) {
  console.error('DBML inválido:', JSON.stringify(e.diags ?? e.message ?? e, null, 1));
  process.exit(1);
}
