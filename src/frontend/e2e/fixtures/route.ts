import type { GeocodingFeature, RouteResult } from '@/types/route'

/** The only geocode suggestion the specs offer; its label is what the combobox settles on. */
export const geocodeResponse: { features: GeocodingFeature[] } = {
  features: [
    {
      geometry: { coordinates: [21.0122, 52.2297] },
      properties: { label: 'Warszawa, Mazowieckie, Poland' },
    },
  ],
}

/**
 * A closed loop of five points. Two constraints are load-bearing: at least two
 * coordinates, or `RouteMap` renders no `Source`; and a `distanceMeters` inside the
 * form's default 30–60 km bounds, so the panel readout is coherent with the request.
 */
export const loopRouteResponse: RouteResult = {
  geometry: {
    coordinates: [
      { longitude: 21.0122, latitude: 52.2297 },
      { longitude: 21.0512, latitude: 52.2601 },
      { longitude: 21.0904, latitude: 52.2312 },
      { longitude: 21.0488, latitude: 52.2015 },
      { longitude: 21.0122, latitude: 52.2297 },
    ],
  },
  distanceMeters: 42_500,
  segments: [
    { fromIndex: 0, toIndex: 2, surface: 'asphalt', roadClass: 'tertiary' },
    { fromIndex: 2, toIndex: 4, surface: 'gravel', roadClass: 'track' },
  ],
  pavedRatio: 0.9,
  smoothnessScore: 0.85,
  overlapRatio: 0.05,
  qualityWarning: false,
  maxConsecutiveSharpTurns: 1,
}

/**
 * Minimal valid MapLibre style. Serving it keeps the map genuinely loading — `onLoad`
 * fires, so `isMapLoaded` gates open and `fitBounds` runs — without reaching the real
 * tile host.
 */
export const stubMapStyle = { version: 8, sources: {}, layers: [] }
