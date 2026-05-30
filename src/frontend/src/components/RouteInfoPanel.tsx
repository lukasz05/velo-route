export default function RouteInfoPanel({ distanceMeters }: { distanceMeters: number }) {
  const km = (distanceMeters / 1000).toFixed(1);
  return (
    <div className="mt-4 rounded-lg border border-zinc-200 bg-zinc-50 p-4">
      <p className="text-sm text-zinc-500">Total distance</p>
      <p className="text-2xl font-semibold text-zinc-900">{km} km</p>
    </div>
  );
}
