'use client';

import dynamic from 'next/dynamic';
import { useState } from 'react';
import RouteForm from './RouteForm';
import RouteInfoPanel from './RouteInfoPanel';
import ErrorMessage from './ErrorMessage';
import { RouteGenerationError } from '@/types/route';
import type { RouteResult } from '@/types/route';

const RouteMap = dynamic(() => import('./RouteMap'), { ssr: false });

interface GenerateParams {
  startLon: number;
  startLat: number;
  minKm: number;
  maxKm: number;
  seed?: number;
}

export default function RouteApp() {
  const [selectedPoint, setSelectedPoint] = useState<{ lon: number; lat: number } | null>(null);
  const [routeResult, setRouteResult] = useState<RouteResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<RouteGenerationError | null>(null);

  async function handleGenerate(params: GenerateParams) {
    setIsLoading(true);
    setError(null);
    try {
      const res = await fetch('/api/routes/loop', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(params),
      });
      if (!res.ok) {
        const body = await res.json() as { error?: string; code?: string };
        throw new RouteGenerationError(body.code ?? 'PROVIDER_ERROR', body.error ?? 'Unknown error');
      }
      const result = await res.json() as RouteResult;
      setRouteResult(result);
    } catch (err) {
      if (err instanceof RouteGenerationError) {
        setError(err);
      } else {
        setError(new RouteGenerationError('PROVIDER_ERROR', 'Something went wrong'));
      }
    } finally {
      setIsLoading(false);
    }
  }

  const routeCoordinates = routeResult?.geometry.coordinates ?? null;
  const pinPoint = routeCoordinates && routeCoordinates.length > 0
    ? { lon: routeCoordinates[0].longitude, lat: routeCoordinates[0].latitude }
    : selectedPoint;

  return (
    <div className="flex flex-col md:h-screen md:flex-row">
      {/* Left panel */}
      <div className="flex w-full flex-col gap-2 bg-white p-6 md:w-96 md:overflow-y-auto md:border-r md:border-zinc-200">
        <h1 className="text-xl font-bold text-zinc-900">VeloRoute</h1>
        <p className="mb-2 text-sm text-zinc-500">Plan a cycling loop route</p>
        <RouteForm
          onGenerate={handleGenerate}
          onStartPointChange={setSelectedPoint}
          isLoading={isLoading}
          hasResult={routeResult !== null}
        />
        <ErrorMessage error={error} />
        {routeResult && <RouteInfoPanel distanceMeters={routeResult.distanceMeters} />}
      </div>

      {/* Right panel — map */}
      <div className="h-[60vh] md:h-full md:flex-1">
        <RouteMap startPoint={pinPoint} routeCoordinates={routeCoordinates} />
      </div>
    </div>
  );
}
