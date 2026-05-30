import { generateLoopRoute } from '@/lib/routingApi';
import { RouteGenerationError } from '@/types/route';

export async function POST(request: Request) {
  let params: unknown;
  try {
    params = await request.json();
  } catch {
    return Response.json({ error: 'Invalid request body', code: 'INVALID_REQUEST' }, { status: 400 });
  }

  try {
    const result = await generateLoopRoute(params as Parameters<typeof generateLoopRoute>[0]);
    return Response.json(result);
  } catch (err) {
    if (err instanceof RouteGenerationError) {
      const status =
        err.code === 'NO_ROUTE' || err.code === 'NO_VALID_RESULT' ? 422
        : err.code === 'RATE_LIMITED' ? 429
        : err.code === 'TIMEOUT' ? 504
        : 502;
      return Response.json({ error: err.message, code: err.code }, { status });
    }
    return Response.json({ error: 'Unexpected error', code: 'PROVIDER_ERROR' }, { status: 502 });
  }
}
