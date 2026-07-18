export async function GET(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const authHeader = request.headers.get('Authorization');
  if (!authHeader) {
    return Response.json({ error: 'Missing Authorization header', code: 'UNAUTHORIZED' }, { status: 401 });
  }

  const { id } = await params;
  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  let res: Response;
  try {
    res = await fetch(`${apiUrl}/routes/${id}`, {
      headers: { Authorization: authHeader },
    });
  } catch {
    return Response.json({ error: 'Could not reach backend', code: 'PROVIDER_ERROR' }, { status: 502 });
  }

  if (!res.ok) {
    let code = 'PROVIDER_ERROR';
    let message = `Backend returned ${res.status}`;
    try {
      const errBody = await res.json() as { error?: string; code?: string };
      if (errBody.code) code = errBody.code;
      if (errBody.error) message = errBody.error;
    } catch { /* ignore parse errors */ }
    return Response.json({ error: message, code }, { status: res.status });
  }

  const resBody = await res.json();
  return Response.json(resBody, { status: res.status });
}
