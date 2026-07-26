import { requireAuthHeader, proxyFetch } from '@/lib/apiProxy';

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function POST(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const { id } = await params;
  if (!GUID_PATTERN.test(id)) {
    return Response.json({ error: 'Invalid route id', code: 'INVALID_ID' }, { status: 400 });
  }

  const res = await proxyFetch(`/routes/${id}/share`, {
    method: 'POST',
    headers: { Authorization: authHeader },
  });
  if (!res.ok) return res;

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}

export async function DELETE(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const { id } = await params;
  if (!GUID_PATTERN.test(id)) {
    return Response.json({ error: 'Invalid route id', code: 'INVALID_ID' }, { status: 400 });
  }

  const res = await proxyFetch(`/routes/${id}/share`, {
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
