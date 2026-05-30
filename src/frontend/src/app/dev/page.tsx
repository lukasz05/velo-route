import { fetchRoutePreview } from '@/lib/routingApi';

export default async function DevPage() {
  try {
    const result = await fetchRoutePreview();
    return (
      <main style={{ padding: '1rem', fontFamily: 'monospace' }}>
        <h1>Route Preview (dev)</h1>
        <pre>{JSON.stringify(result, null, 2)}</pre>
      </main>
    );
  } catch (err) {
    return (
      <main style={{ padding: '1rem', fontFamily: 'monospace' }}>
        <h1>Route Preview (dev)</h1>
        <p style={{ color: 'red' }}>
          Failed to fetch route: {err instanceof Error ? err.message : String(err)}
        </p>
      </main>
    );
  }
}
