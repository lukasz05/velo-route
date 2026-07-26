const TOKEN_PATTERN = /^[A-Za-z0-9]{12}$/;

export async function GET(_request: Request, { params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  if (!TOKEN_PATTERN.test(token)) {
    return Response.json({ error: 'Invalid share token', code: 'INVALID_TOKEN' }, { status: 400 });
  }

  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  let res: Response;
  try {
    res = await fetch(`${apiUrl}/shares/${token}`);
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
