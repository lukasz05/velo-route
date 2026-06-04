export async function POST(request: Request) {
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return Response.json({ error: 'Invalid request body', code: 'INVALID_REQUEST' }, { status: 400 });
  }

  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  let res: Response;
  try {
    res = await fetch(`${apiUrl}/routes/gpx`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
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

  const gpxText = await res.text();
  return new Response(gpxText, {
    headers: { 'Content-Type': 'application/gpx+xml' },
  });
}
