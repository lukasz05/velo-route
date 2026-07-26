export function requireAuthHeader(request: Request): string | Response {
  const authHeader = request.headers.get('Authorization');
  if (!authHeader) {
    return Response.json({ error: 'Missing Authorization header', code: 'UNAUTHORIZED' }, { status: 401 });
  }
  return authHeader;
}

/**
 * Forwards a request to the backend and, on a non-2xx response, builds the
 * standard { error, code } relay body. Callers still handle their own
 * success-path body shape (json vs text, 204-vs-body, etc).
 */
export async function proxyFetch(path: string, init?: RequestInit): Promise<Response> {
  const apiUrl = process.env.VELO_API_URL ?? 'http://localhost:5098';
  let res: Response;
  try {
    res = init ? await fetch(`${apiUrl}${path}`, init) : await fetch(`${apiUrl}${path}`);
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

  return res;
}
