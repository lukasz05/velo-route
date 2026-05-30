import { notFound } from 'next/navigation';
import { fetchRoutePreview } from '@/lib/routingApi';

export default async function DevPage() {
  if (process.env.NODE_ENV !== 'development') notFound();
  try {
    const result = await fetchRoutePreview();
    return (
      <main style={{ padding: '1rem', fontFamily: 'monospace' }}>
        <h1>Route Preview (dev)</h1>
        <pre>{JSON.stringify(result, null, 2)}</pre>
      </main>
    );
  } catch {
    return (
      <main style={{ padding: '1rem', fontFamily: 'monospace' }}>
        <h1>Route Preview (dev)</h1>
        <p style={{ color: 'red' }}>
          Failed to fetch route preview. Check backend logs for details.
        </p>
      </main>
    );
  }
}
