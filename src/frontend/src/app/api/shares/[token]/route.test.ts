import { describe, it, expect, vi, afterEach } from 'vitest';
import { GET } from './route';

function makeParams(token: string) {
  return { params: Promise.resolve({ token }) };
}

describe('GET /api/shares/[token]', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns 400 for a malformed token instead of forwarding it to the backend', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/shares/not-valid!!');

    const res = await GET(request, makeParams('not-valid!!'));

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.code).toBe('INVALID_TOKEN');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('forwards the request with no Authorization header and relays a 200', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: '11111111-1111-1111-1111-111111111111', name: 'Loop', shareToken: 'abc123XYZ789' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/shares/abc123XYZ789');

    const res = await GET(request, makeParams('abc123XYZ789'));

    expect(fetchMock).toHaveBeenCalledWith('http://localhost:5098/shares/abc123XYZ789');
    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body).toEqual({ id: '11111111-1111-1111-1111-111111111111', name: 'Loop', shareToken: 'abc123XYZ789' });
  });

  it('relays a 404 from the backend', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Route not found', code: 'NOT_FOUND' }), { status: 404 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/shares/abc123XYZ789');

    const res = await GET(request, makeParams('abc123XYZ789'));

    expect(res.status).toBe(404);
    const body = await res.json();
    expect(body).toEqual({ error: 'Route not found', code: 'NOT_FOUND' });
  });
});
