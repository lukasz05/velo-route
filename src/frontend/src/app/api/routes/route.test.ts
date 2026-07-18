import { describe, it, expect, vi, afterEach } from 'vitest';
import { POST } from './route';

describe('POST /api/routes', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns 401 when Authorization header is missing', async () => {
    const request = new Request('http://localhost/api/routes', {
      method: 'POST',
      body: JSON.stringify({ name: 'Test' }),
    });

    const res = await POST(request);

    expect(res.status).toBe(401);
    const body = await res.json();
    expect(body.code).toBe('UNAUTHORIZED');
  });

  it('forwards the Authorization header and body, and relays a 201', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: 'abc-123' }), { status: 201 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const payload = { name: 'My Loop', distanceKm: 42, coordinates: [{ longitude: 1, latitude: 2 }] };
    const request = new Request('http://localhost/api/routes', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
      body: JSON.stringify(payload),
    });

    const res = await POST(request);

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5098/routes',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({
          Authorization: 'Bearer test-token',
          'Content-Type': 'application/json',
        }),
        body: JSON.stringify(payload),
      }),
    );
    expect(res.status).toBe(201);
    const body = await res.json();
    expect(body).toEqual({ id: 'abc-123' });
  });

  it('relays a backend error response with its error and code', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Name is required', code: 'INVALID_INPUT' }), { status: 400 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
      body: JSON.stringify({ name: '' }),
    });

    const res = await POST(request);

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body).toEqual({ error: 'Name is required', code: 'INVALID_INPUT' });
  });
});
