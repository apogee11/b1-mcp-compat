// End-to-end check of the SAP B1 MCP server through b1-mcp-compat. Business data read: one row of Currencies (non-personal).
//   node e2e-client.mjs http://localhost:3100/mcp report.json
import fs from 'node:fs';
// The MCP SDK comes from b1-mcp-server/node_modules; without it, from an `npm install @modelcontextprotocol/sdk` next to this file.
const sdk = new URL('../../b1-mcp-server/node_modules/@modelcontextprotocol/sdk/dist/esm/', import.meta.url);
const load = async (f) => fs.existsSync(new URL(f, sdk)) ? import(new URL(f, sdk).href) : import(`@modelcontextprotocol/sdk/${f.replace(/\.js$/, '')}`);
const { Client } = await load('client/index.js');
const { StreamableHTTPClientTransport } = await load('client/streamableHttp.js');

// --quick: skip the sweep of every entity set (~500 calls, which the MCP server's default rate limit of 120 per minute would reject).
const quick = process.argv.includes('--quick');
const [endpoint = 'http://localhost:3100/mcp', reportPath = 'e2e-report.json'] = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const results = [];
const check = (name, ok, detail = '') => { results.push({ name, ok: !!ok, detail }); console.log(`[${ok ? 'PASS' : 'FAIL'}] ${name}${detail ? ' - ' + detail : ''}`); };
const text = (r) => (r.content ?? []).map((c) => c.text ?? '').join('\n');

const client = new Client({ name: 'b1-mcp-compat-e2e', version: '0.1.0' }, { capabilities: {} });
try {
  await client.connect(new StreamableHTTPClientTransport(new URL(endpoint)));
  check('MCP handshake', true, JSON.stringify(client.getServerVersion()));
  const tools = (await client.listTools()).tools.map((t) => t.name);
  check('tools listed', ['b1_find_entities', 'b1_get_entity_schema', 'b1_read', 'b1_write'].every((t) => tools.includes(t)), tools.join(', '));
  const call = async (name, args) => { try { const r = await client.callTool({ name, arguments: args }); return { err: !!r.isError, t: text(r), s: r.structuredContent }; } catch (e) { return { err: true, t: String(e.message) }; } };

  const f = await call('b1_find_entities', { query: 'Order', limit: 10 });
  check('find_entities "Order" returns Orders', !f.err && /\bOrders\b/.test(f.t));
  const all = await call('b1_find_entities', { category: 'all', limit: 50 });
  check('find_entities lists entities', !all.err && /STEP 1/.test(all.t));

  const bp = await call('b1_get_entity_schema', { entityName: 'BusinessPartners' });
  const bpProps = Number((bp.t.match(/Total Properties: (\d+)/) || [])[1] ?? 0);
  check('schema BusinessPartners', !bp.err && bpProps > 50, `${bpProps} properties`);
  const personal = (bp.t.match(/personal/gi) || []).length;
  check('BusinessPartners carries personal-data markers', !bp.err && personal > 0, `${personal} markers`);
  const orders = await call('b1_get_entity_schema', { entityName: 'Orders' });
  check('schema Orders', !orders.err && /Total Properties/.test(orders.t));
  const line = await call('b1_get_entity_schema', { entityName: 'Orders', structuralTypeName: 'DocumentLine' });
  check('schema Orders/DocumentLine drill-down', !line.err && /Properties/.test(line.t));

  // a user table or object, if the company has any
  const ud = await call('b1_find_entities', { category: 'user-defined-table', limit: 50 });
  const udName = (ud.t.match(/^\s+- (\S+)\s+\[user-defined/m) || [])[1];
  if (udName) { const us = await call('b1_get_entity_schema', { entityName: udName }); check(`schema user-defined entity ${udName}`, !us.err && /Properties/.test(us.t), us.err ? us.t.slice(0, 200) : ''); }
  else check('user-defined entity (none in this company)', true, 'skipped');

  // every entity set: a property the connector cannot explain fails its entity set alone
  // SAP's stock server caps `limit` at 50; if it rejects a bigger one, sweep by letter and digit instead (may miss entity sets).
  if (!quick) {
  const namesOf = (r) => r.s?.matches?.map((m) => m.entityName) ?? [];
  let names =namesOf(await call('b1_find_entities', { category: 'all', limit: 100000 }));
  if (!names.length) {
    const found = new Set();
    for (const q of 'abcdefghijklmnopqrstuvwxyz0123456789') namesOf(await call('b1_find_entities', { category: 'all', query: q, limit: 50 })).forEach((n) => found.add(n));
    names = [...found];
    console.log(`       stock MCP server: limit capped at 50, swept ${names.length} entity sets by letter (raise the max in b1-tool-registry.ts for full coverage)`);
  }
  const bad = [];
  let next = 0;
  await Promise.all(Array.from({ length: 8 }, async () => {
    while (next < names.length) {
      const n = names[next++];
      const r = await call('b1_get_entity_schema', { entityName: n });
      if (r.err || !/Total Properties/.test(r.t)) bad.push(`${n}: ${r.t.slice(0, 300).replace(/\s+/g, ' ')}`);
    }
  }));
  check(`schema of every entity set (${names.length})`, names.length > 0 && !bad.length, bad.length ? `${bad.length} failed` : '');
  bad.forEach((b) => console.log(`       ${b}`));
  }

  const cur = await call('b1_read', { entityName: 'Currencies', operation: 'read', selectString: 'Code', topNumber: 1 });
  check('read Currencies (top 1)', !cur.err && /SUCCESS/.test(cur.t), cur.err ? cur.t.slice(0, 200) : '');
} catch (e) {
  check('e2e run', false, String(e.message).slice(0, 300));
} finally {
  try { await client.close(); } catch {}
}
const failed = results.filter((r) => !r.ok).length;
fs.writeFileSync(reportPath, JSON.stringify({ endpoint, at: new Date().toISOString(), passed: results.length - failed, failed, results }, null, 1));
console.log(`\n${results.length - failed}/${results.length} checks passed -> ${reportPath}`);
process.exit(failed ? 1 : 0);
