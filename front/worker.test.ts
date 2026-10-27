import assert from 'node:assert/strict';
import test from 'node:test';

import worker from './worker.ts';

test('SPA routes work, missing assets and unimplemented API paths return 404', async () => {
  const env = { ASSETS: { fetch: async () => new Response('<html>shell</html>', {
    headers: { 'Content-Type': 'text/html', 'Cache-Control': 'public, max-age=3600' },
  }) } };
  const route = await worker.fetch(new Request('https://erpflow.test/orders/new'), env);
  assert.equal(route.status, 200);
  assert.equal(route.headers.get('Cache-Control'), 'no-cache');
  for (const path of ['/missing.js', '/icons/missing.png', '/ngsw.json', '/api', '/api/sales-orders']) {
    assert.equal((await worker.fetch(new Request(`https://erpflow.test${path}`), env)).status, 404, path);
  }
});

test('worker manifests revalidate while real static assets retain their headers', async () => {
  const env = { ASSETS: { fetch: async () => new Response('{}', {
    headers: { 'Content-Type': 'application/json', 'Cache-Control': 'public, max-age=3600' },
  }) } };
  const manifest = await worker.fetch(new Request('https://erpflow.test/ngsw.json'), env);
  assert.equal(manifest.headers.get('Cache-Control'), 'no-cache');
  const chunk = await worker.fetch(new Request('https://erpflow.test/chunk-HASH.js'), env);
  assert.equal(chunk.headers.get('Cache-Control'), 'public, max-age=3600');
  assert.equal(await chunk.text(), '{}');
});
