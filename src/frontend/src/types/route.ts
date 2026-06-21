export interface RouteCoordinate {
  longitude: number;
  latitude: number;
}

export interface RouteWaySegment {
  fromIndex: number;
  toIndex: number;
  surface: string;
  roadClass: string;
}

export interface RouteGeometry {
  coordinates: RouteCoordinate[];
}

export interface RouteResult {
  geometry: RouteGeometry;
  distanceMeters: number;
  segments: RouteWaySegment[];
  pavedRatio: number;
  smoothnessScore: number;
}

export interface GeocodingFeature {
  geometry: { coordinates: [number, number] };
  properties: { label: string };
}

export interface LoopRouteRequest {
  startLon: number;
  startLat: number;
  minKm: number;
  maxKm: number;
  seed?: number;
}

export class RouteGenerationError extends Error {
  constructor(public readonly code: string, message: string) {
    super(message);
    this.name = 'RouteGenerationError';
  }
}
