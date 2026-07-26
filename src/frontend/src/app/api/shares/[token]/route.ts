import { proxyFetch } from '@/lib/apiProxy';

const TOKEN_PATTERN = /^[A-Za-z0-9]{12}$/;

export async function GET(_request: Request, { params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  if (!TOKEN_PATTERN.test(token)) {
    return Response.json({ error: 'Invalid share token', code: 'INVALID_TOKEN' }, { status: 400 });
  }

  const res = await proxyFetch(`/shares/${token}`);
  if (!res.ok) return res;

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}
