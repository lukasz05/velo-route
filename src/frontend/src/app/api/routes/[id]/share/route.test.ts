import { describe, it, expect, vi, afterEach } from 'vitest';
import { POST, DELETE } from './route';

function makeParams(id: string) {
  return { params: Promise.resolve({ id }) };
}

describe('POST /api/routes/[id]/share', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns 401 when Authorization header is missing', async () => {
    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111/share', {
      method: 'POST',
    });

    const res = await POST(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(res.status).toBe(401);
    const body = await res.json();
    expect(body.code).toBe('UNAUTHORIZED');
  });

  it('returns 400 for a malformed id instead of forwarding it to the backend', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/not-a-guid/share', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await POST(request, makeParams('not-a-guid'));

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.code).toBe('INVALID_ID');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('forwards the Authorization header and relays a 201 with the token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'abc123XYZ789' }), { status: 201 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111/share', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await POST(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5098/routes/11111111-1111-1111-1111-111111111111/share',
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      }),
    );
    expect(res.status).toBe(201);
    const body = await res.json();
    expect(body).toEqual({ token: 'abc123XYZ789' });
  });

  it('relays a 200 with the same token on idempotent re-share', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'abc123XYZ789' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111/share', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await POST(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body).toEqual({ token: 'abc123XYZ789' });
  });

  it('relays a 404 from the backend', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Route not found', code: 'NOT_FOUND' }), { status: 404 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/22222222-2222-2222-2222-222222222222/share', {
      method: 'POST',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await POST(request, makeParams('22222222-2222-2222-2222-222222222222'));

    expect(res.status).toBe(404);
    const body = await res.json();
    expect(body).toEqual({ error: 'Route not found', code: 'NOT_FOUND' });
  });
});

describe('DELETE /api/routes/[id]/share', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns 401 when Authorization header is missing', async () => {
    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111/share', {
      method: 'DELETE',
    });

    const res = await DELETE(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(res.status).toBe(401);
    const body = await res.json();
    expect(body.code).toBe('UNAUTHORIZED');
  });

  it('returns 400 for a malformed id instead of forwarding it to the backend', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/not-a-guid/share', {
      method: 'DELETE',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await DELETE(request, makeParams('not-a-guid'));

    expect(res.status).toBe(400);
    const body = await res.json();
    expect(body.code).toBe('INVALID_ID');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('forwards DELETE and the Authorization header, relaying a 204', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/11111111-1111-1111-1111-111111111111/share', {
      method: 'DELETE',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await DELETE(request, makeParams('11111111-1111-1111-1111-111111111111'));

    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5098/routes/11111111-1111-1111-1111-111111111111/share',
      expect.objectContaining({
        method: 'DELETE',
        headers: expect.objectContaining({ Authorization: 'Bearer test-token' }),
      }),
    );
    expect(res.status).toBe(204);
  });

  it('relays a 404 from the backend when no active share exists', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ error: 'Share not found', code: 'NOT_FOUND' }), { status: 404 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    const request = new Request('http://localhost/api/routes/22222222-2222-2222-2222-222222222222/share', {
      method: 'DELETE',
      headers: { Authorization: 'Bearer test-token' },
    });

    const res = await DELETE(request, makeParams('22222222-2222-2222-2222-222222222222'));

    expect(res.status).toBe(404);
    const body = await res.json();
    expect(body).toEqual({ error: 'Share not found', code: 'NOT_FOUND' });
  });
});
