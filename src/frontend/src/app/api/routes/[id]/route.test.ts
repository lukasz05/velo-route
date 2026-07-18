import { describe, it, expect, vi, afterEach } from 'vitest';
import { GET } from './route';

function makeParams(id: string) {
  return { params: Promise.resolve({ id }) };
}

describe('GET /api/routes/[id]', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns 401 when Authorization header is missing', async () => {
    const request = new Request('http://localhost/api/routes/abc-123');

    const res = await GET(request, makeParams('abc-123'));

    expect(res.status).toBe(401);
    const body = await res.json();
    expect(body.code).toBe('UNAUTHORIZED');
  });

  it('forwards the Authorization header and relays the backend body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 'abc-123', name: 'Loop' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/abc-123', {
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await GET(request, makeParams('abc-123'));

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5098/routes/abc-123',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      }),
    );
    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body).toEqual({ id: 'abc-123', name: 'Loop' });
  });

  it('relays a 404 from the backend', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Route not found', code: 'NOT_FOUND' }), { status: 404 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/missing', {
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await GET(request, makeParams('missing'));

    expect(res.status).toBe(404);
    const body = await res.json();
    expect(body).toEqual({ error: 'Route not found', code: 'NOT_FOUND' });
  });
});
