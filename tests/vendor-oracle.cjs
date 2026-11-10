// Run against a downloaded official driver bundle. Evaluates only the pure macro encoder.
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync(process.argv[2], 'utf8');
function extract(name) {
  const at = source.indexOf('function ' + name + '(');
  if (at < 0) throw Error('Vendor function missing: ' + name);
  let start = source.indexOf('{', at), depth = 1, i = start + 1;
  for (; depth; i++) { if (source[i] === '{') depth++; if (source[i] === '}') depth--; }
  return source.slice(at, i);
}
const context = { Uint8Array, We: { String_To_UTF8: s => new TextEncoder().encode(s) } };
vm.createContext(context);
vm.runInContext(extract('rt') + '\nasync ' + extract('v3'), context);
(async () => {
  const macro = await context.v3({ name: '\u0422\u0435\u0441\u0442', contexts: [
    { status: 0, type: 1, value: 4, delay: 20 }, { status: 1, type: 1, value: 4, delay: 100 }
  ] });
  fs.writeFileSync('tests/vendor-fixtures.json', JSON.stringify({
    source: 'https://controlhub.top/AttackShark/assets/index-CxS_PH0T.js', retrieved: '2026-11-10',
    macroHex: Buffer.from(macro).toString('hex').toUpperCase()
  }, null, 2));
})();
