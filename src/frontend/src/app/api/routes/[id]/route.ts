import { requireAuthHeader, proxyFetch } from '@/lib/apiProxy';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function GET(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const { id } = await params;
  if (!GUID_PATTERN.test(id)) {
    return Response.json({ error: 'Invalid route id', code: 'INVALID_ID' }, { status: 400 });
  }

  const res = await proxyFetch(`/routes/${id}`, {
    headers: { Authorization: authHeader },
  });
  if (!res.ok) return res;

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}

export async function PATCH(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const { id } = await params;
  if (!GUID_PATTERN.test(id)) {
    return Response.json({ error: 'Invalid route id', code: 'INVALID_ID' }, { status: 400 });
  }

  const rawBody = await request.text();
  let body: unknown = {};
  if (rawBody.trim() !== '') {
    try {
      body = JSON.parse(rawBody);
    } catch {
      return Response.json({ error: 'Invalid request body', code: 'INVALID_REQUEST' }, { status: 400 });
    }
  }

  const res = await proxyFetch(`/routes/${id}`, {
    method: 'PATCH',
    headers: { Authorization: authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) return res;

  return new Response(null, { status: 204 });
}

export async function DELETE(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const { id } = await params;
  if (!GUID_PATTERN.test(id)) {
    return Response.json({ error: 'Invalid route id', code: 'INVALID_ID' }, { status: 400 });
  }

  const res = await proxyFetch(`/routes/${id}`, {
    method: 'DELETE',
    headers: { Authorization: authHeader },
  });
  if (!res.ok) return res;

  if (res.status === 204) {
    return new Response(null, { status: 204 });
  }
  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}
