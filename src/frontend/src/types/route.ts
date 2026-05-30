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
}
