interface Assets {
  fetch(request: Request): Promise<Response>;
}

export default {
  async fetch(request: Request, env: { ASSETS: Assets }): Promise<Response> {
    const path = new URL(request.url).pathname;
    const response = await env.ASSETS.fetch(request);
    const html = response.headers.get('Content-Type')?.includes('text/html');
    const api = path === '/api' || path.startsWith('/api/');
    const asset = /\.[^/]+$/.test(path) && !path.endsWith('.html');
    if (api || (html && asset)) return new Response('Not found', { status: 404 });

    if (html || ['/ngsw.json', '/ngsw-worker.js', '/safety-worker.js', '/manifest.webmanifest'].includes(path)) {
      const fresh = new Response(response.body, response);
      fresh.headers.set('Cache-Control', 'no-cache');
      return fresh;
    }
    return response;
  },
};
