// Parity check: the same MCP tool calls against two MCP servers, compared.
//   native    = MCP server -> native FP2608 Service Layer
//   candidate = MCP server -> b1-mcp-compat (.NET, SimulateLegacy=true) -> native FP2608 Service Layer
//
//   node compare.mjs --native http://127.0.0.1:51001/mcp --candidate http://127.0.0.1:51002/mcp [--verbose]
//
// Prints IDENTICAL / DIFF per call (with a short line diff) and exits 1 if anything differs.
// Only metadata and one row of Currencies (non-personal) are read.
import { parseArgs } from 'node:util';

const { values } = parseArgs({ options: { native: { type: 'string' }, candidate: { type: 'string' }, verbose: { type: 'boolean', default: false } } });
if (!values.native || !values.candidate) { console.error('usage: node compare.mjs --native <mcp url> --candidate <mcp url> [--verbose]'); process.exit(2); }

const sdk = new URL('../../b1-mcp-server/node_modules/@modelcontextprotocol/sdk/dist/esm/', import.meta.url);
const { Client } = await import(new URL('client/index.js', sdk).href);
const { StreamableHTTPClientTransport } = await import(new URL('client/streamableHttp.js', sdk).href);

const CASES = [
  ['b1_find_entities query=Order category=sales', 'b1_find_entities', { query: 'Order', category: 'sales', limit: 50 }],
  ['b1_find_entities category=user-defined-table', 'b1_find_entities', { category: 'user-defined-table', limit: 50 }],
  ['b1_find_entities category=user-defined-object', 'b1_find_entities', { category: 'user-defined-object', limit: 50 }],
  ['b1_get_entity_schema BusinessPartners (UDFs)', 'b1_get_entity_schema', { entityName: 'BusinessPartners' }],
  ['b1_get_entity_schema Orders', 'b1_get_entity_schema', { entityName: 'Orders' }],
  ['b1_get_entity_schema Orders / DocumentLine', 'b1_get_entity_schema', { entityName: 'Orders', structuralTypeName: 'DocumentLine' }],
  ['b1_get_entity_schema U_SBOCTXT (user table)', 'b1_get_entity_schema', { entityName: 'U_SBOCTXT' }],
  ['b1_get_entity_schema SBOUDT (user object)', 'b1_get_entity_schema', { entityName: 'SBOUDT' }],
  ['b1_read Currencies top 1', 'b1_read', { entityName: 'Currencies', operation: 'read', selectString: 'Code', topNumber: 1 }],
];

// The only legitimate difference in data answers: the OData context URL names the endpoint that served it.
const normalize = (t) => t.replace(/https?:\/\/(?:localhost|127\.0\.0\.1):\d+/g, 'http://SERVICE-LAYER');

async function run(url) {
  const client = new Client({ name: 'b1-mcp-compat-parity', version: '0.1.0' }, { capabilities: {} });
  await client.connect(new StreamableHTTPClientTransport(new URL(url)));
  const out = [];
  for (const [label, tool, args] of CASES) {
    try {
      const r = await client.callTool({ name: tool, arguments: args });
      out.push({ label, err: !!r.isError, text: (r.content ?? []).map((c) => c.text ?? '').join('\n') });
    } catch (e) { out.push({ label, err: true, text: 'EXCEPTION ' + e.message }); }
  }
  await client.close().catch(() => {});
  return out;
}

function lineDiff(a, b, max = 8) {
  const A = a.split('\n'), B = b.split('\n'); const out = [];
  const setB = new Set(B), setA = new Set(A);
  for (const l of A) if (!setB.has(l) && out.length < max) out.push('  native    - ' + l.slice(0, 160));
  for (const l of B) if (!setA.has(l) && out.length < max * 2) out.push('  candidate + ' + l.slice(0, 160));
  if (!out.length) out.push('  (same lines, different order or count: native ' + A.length + ' lines, candidate ' + B.length + ' lines)');
  return out.join('\n');
}

const [nat, cand] = [await run(values.native), await run(values.candidate)];
let diffs = 0;
console.log('call'.padEnd(52), 'result'.padEnd(10), 'native/candidate chars');
for (let i = 0; i < CASES.length; i++) {
  const a = nat[i], b = cand[i]; const na = normalize(a.text), nb = normalize(b.text);
  const same = na === nb && a.err === b.err;
  if (!same) diffs++;
  console.log(a.label.padEnd(52), (same ? 'IDENTICAL' : 'DIFF').padEnd(10), `${a.text.length}/${b.text.length}${a.err || b.err ? '  (error flag: native=' + a.err + ' candidate=' + b.err + ')' : ''}`);
  if (!same) console.log(lineDiff(na, nb));
  else if (values.verbose) console.log('  ' + na.slice(0, 200).replace(/\n/g, ' '));
}
console.log(`\n${CASES.length - diffs}/${CASES.length} identical`);
process.exit(diffs ? 1 : 0);
