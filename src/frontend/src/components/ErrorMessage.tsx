import { RouteGenerationError } from '@/types/route';

const messages: Record<string, string> = {
  NO_ROUTE: 'No road route found — try a different start point or adjust the distance range.',
  RATE_LIMITED: 'Too many requests — please try again in a minute.',
  TIMEOUT: 'Route generation timed out — please try again.',
  NO_VALID_RESULT: 'Couldn\'t find a suitable loop — try a wider distance range.',
};

export default function ErrorMessage({ error }: { error: RouteGenerationError | null }) {
  if (!error) return null;
  const message = messages[error.code] ?? 'Something went wrong — please try again.';
  return (
    <p role="alert" className="mt-2 text-sm text-red-600">
      {message}
    </p>
  );
}
