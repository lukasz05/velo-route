'use client';

import { useState } from 'react';
import SearchBar from './SearchBar';
import type { GeocodingFeature } from '@/types/route';

interface GenerateParams {
  startLon: number;
  startLat: number;
  minKm: number;
  maxKm: number;
  seed?: number;
}

interface RouteFormProps {
  onGenerate: (params: GenerateParams) => void;
  onStartPointChange: (point: { lon: number; lat: number } | null) => void;
  isLoading: boolean;
  hasResult: boolean;
}

export default function RouteForm({ onGenerate, onStartPointChange, isLoading, hasResult }: RouteFormProps) {
  const [startPoint, setStartPoint] = useState<{ lon: number; lat: number } | null>(null);
  const [minKm, setMinKm] = useState(30);
  const [maxKm, setMaxKm] = useState(60);

  function handleSelect(feature: GeocodingFeature) {
    const [lon, lat] = feature.geometry.coordinates;
    setStartPoint({ lon, lat });
    onStartPointChange({ lon, lat });
  }

  const isValid = startPoint !== null && minKm >= 5 && maxKm <= 300 && minKm < maxKm;

  function handleGenerate(e: React.FormEvent) {
    e.preventDefault();
    if (!startPoint || !isValid) return;
    onGenerate({ startLon: startPoint.lon, startLat: startPoint.lat, minKm, maxKm });
  }

  function handleReroll() {
    if (!startPoint || !isValid) return;
    onGenerate({ startLon: startPoint.lon, startLat: startPoint.lat, minKm, maxKm, seed: Math.floor(Math.random() * 360) });
  }

  return (
    <form onSubmit={handleGenerate} className="flex flex-col gap-4">
      <div>
        <label className="mb-1 block text-sm font-medium text-zinc-700">Start location</label>
        <SearchBar onSelect={handleSelect} />
      </div>

      <div className="flex gap-3">
        <div className="flex-1">
          <label htmlFor="min-km" className="mb-1 block text-sm font-medium text-zinc-700">Min km</label>
          <input
            id="min-km"
            type="number"
            min={5}
            max={299}
            value={minKm}
            onChange={e => setMinKm(Number(e.target.value))}
            className="w-full rounded-lg border border-zinc-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
        <div className="flex-1">
          <label htmlFor="max-km" className="mb-1 block text-sm font-medium text-zinc-700">Max km</label>
          <input
            id="max-km"
            type="number"
            min={6}
            max={300}
            value={maxKm}
            onChange={e => setMaxKm(Number(e.target.value))}
            className="w-full rounded-lg border border-zinc-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
      </div>

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={!isValid || isLoading}
          className="flex-1 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isLoading ? 'Generating…' : 'Generate'}
        </button>
        {hasResult && (
          <button
            type="button"
            onClick={handleReroll}
            disabled={isLoading}
            className="rounded-lg border border-zinc-300 px-4 py-2 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Re-roll
          </button>
        )}
      </div>
    </form>
  );
}
