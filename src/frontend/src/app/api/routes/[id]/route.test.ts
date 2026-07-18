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
    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111');

    const res = await GET(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(res.status).toBe(401);
    const body = await res.json();
    expect(body.code).toBe('UNAUTHORIZED');
  });

  it('forwards the Authorization header and relays the backend body', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: '11111111-1111-1111-1111-111111111111', name: 'Loop' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111', {
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await GET(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5098/routes/11111111-1111-1111-1111-111111111111',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      }),
    );
    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body).toEqual({ id: '11111111-1111-1111-1111-111111111111', name: 'Loop' });
  });

  it('relays a 404 from the backend', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Route not found', code: 'NOT_FOUND' }), { status: 404 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/22222222-2222-2222-2222-222222222222', {
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await GET(request, makeParams('22222222-2222-2222-2222-222222222222'));

    expect(res.status).toBe(404);
    const body = await res.json();
    expect(body).toEqual({ error: 'Route not found', code: 'NOT_FOUND' });
  });

  it('returns 400 for a malformed id instead of forwarding it to the backend', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/not-a-guid', {
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await GET(request, makeParams('not-a-guid'));

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.code).toBe('INVALID_ID');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
