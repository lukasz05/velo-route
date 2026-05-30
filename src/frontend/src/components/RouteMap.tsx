'use client';

import { useEffect, useRef } from 'react';
import { Map as ReactMap, Marker, Source, Layer } from '@vis.gl/react-maplibre';
import type { MapRef } from '@vis.gl/react-maplibre';
import type { LngLatBoundsLike } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

interface RouteMapProps {
  startPoint: { lon: number; lat: number } | null;
  routeCoordinates: Array<{ longitude: number; latitude: number }> | null;
}

export default function RouteMap({ startPoint, routeCoordinates }: RouteMapProps) {
  const mapRef = useRef<MapRef>(null);

  useEffect(() => {
    if (!routeCoordinates || routeCoordinates.length < 2) return;
    const map = mapRef.current;
    if (!map) return;
    const lngs = routeCoordinates.map(c => c.longitude);
    const lats = routeCoordinates.map(c => c.latitude);
    const bounds: LngLatBoundsLike = [
      [Math.min(...lngs), Math.min(...lats)],
      [Math.max(...lngs), Math.max(...lats)],
    ];
    map.fitBounds(bounds, { padding: 50, duration: 800 });
  }, [routeCoordinates]);

  useEffect(() => {
    if (routeCoordinates) return; // route view takes priority
    if (!startPoint) return;
    mapRef.current?.flyTo({ center: [startPoint.lon, startPoint.lat], zoom: 11, duration: 800 });
  }, [startPoint, routeCoordinates]);

  const geojson: GeoJSON.FeatureCollection | null = routeCoordinates && routeCoordinates.length >= 2
    ? {
        type: 'FeatureCollection',
        features: [{
          type: 'Feature',
          geometry: {
            type: 'LineString',
            coordinates: routeCoordinates.map(c => [c.longitude, c.latitude]),
          },
          properties: {},
        }],
      }
    : null;

  const pin = routeCoordinates && routeCoordinates.length > 0
    ? { lon: routeCoordinates[0].longitude, lat: routeCoordinates[0].latitude }
    : startPoint;

  return (
    <ReactMap
      ref={mapRef}
      initialViewState={{ longitude: 16.37, latitude: 48.21, zoom: 10 }}
      style={{ width: '100%', height: '100%' }}
      mapStyle="https://tiles.openfreemap.org/styles/liberty"
    >
      {geojson && (
        <Source id="route" type="geojson" data={geojson}>
          <Layer
            id="route-line"
            type="line"
            paint={{ 'line-color': '#2563eb', 'line-width': 4 }}
          />
        </Source>
      )}
      {pin && (
        <Marker longitude={pin.lon} latitude={pin.lat} />
      )}
    </ReactMap>
  );
}
