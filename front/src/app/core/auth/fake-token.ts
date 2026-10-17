/** Test helper: an unsigned JWT with the given claims. */
export function fakeToken(claims: Record<string, unknown>): string {
  const b64 = (value: unknown) =>
    btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(value))))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
  const exp = Math.floor(Date.now() / 1000) + 3600;
  return `${b64({ alg: 'none' })}.${b64({ exp, ...claims })}.sig`;
}
