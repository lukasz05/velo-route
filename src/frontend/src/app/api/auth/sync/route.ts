export async function POST(request: Request) {
  const authHeader = request.headers.get('Authorization');
  if (!authHeader) {
    return Response.json({ error: 'Missing Authorization header', code: 'UNAUTHORIZED' }, { status: 401 });
  }

  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  let res: Response;
  try {
    res = await fetch(`${apiUrl}/auth/sync`, {
      method: 'POST',
      headers: { Authorization: authHeader },
    });
  } catch {
    return Response.json({ error: 'Could not reach backend', code: 'PROVIDER_ERROR' }, { status: 502 });
  }

  return new Response(null, { status: res.status });
}
