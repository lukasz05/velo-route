import { requireAuthHeader, proxyFetch } from '@/lib/apiProxy';

export async function DELETE(request: Request) {
  const authHeader = requireAuthHeader(request);
  if (authHeader instanceof Response) return authHeader;

  const res = await proxyFetch('/account', {
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
