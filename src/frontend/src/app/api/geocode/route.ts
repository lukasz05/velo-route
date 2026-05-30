export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const q = searchParams.get('q') ?? '';

  if (q.length < 2) {
    return Response.json({ features: [] });
  }

  const apiKey = process.env.ORS_API_KEY;
  if (!apiKey) {
    return Response.json({ features: [] });
  }

  try {
    const orsUrl = `https://api.openrouteservice.org/geocode/autocomplete?text=${encodeURIComponent(q)}&api_key=${apiKey}&size=5`;
    const res = await fetch(orsUrl, { cache: 'no-store' });
    if (!res.ok) {
      console.error(`[geocode] ORS returned ${res.status} for q="${q}"`);
      return Response.json({ features: [] });
    }
    const data = await res.json();
    return Response.json(data);
  } catch (err) {
    console.error('[geocode] fetch error:', err);
    return Response.json({ features: [] });
  }
}
