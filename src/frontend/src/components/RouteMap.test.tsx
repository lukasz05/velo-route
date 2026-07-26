import { act } from 'react';
import { render } from '@testing-library/react';
import { describe, it, expect, vi, afterEach } from 'vitest';
import type { ReactNode, Ref } from 'react';
import RouteMap from './RouteMap';

const mapMethods = {
  fitBounds: vi.fn(),
  flyTo: vi.fn(),
  resize: vi.fn(),
};

let capturedOnLoad: (() => void) | undefined;

vi.mock('@vis.gl/react-maplibre', () => ({
  Map: ({ children, onLoad, ref }: { children?: ReactNode; onLoad?: () => void; ref?: Ref<typeof mapMethods> }) => {
    capturedOnLoad = onLoad;
    if (typeof ref === 'function') ref(mapMethods);
    else if (ref) ref.current = mapMethods;
    return <div>{children}</div>;
  },
  Marker: () => null,
  Source: ({ children }: { children?: ReactNode }) => <>{children}</>,
  Layer: () => null,
}));

vi.mock('maplibre-gl/dist/maplibre-gl.css', () => ({}));

describe('RouteMap', () => {
  afterEach(() => {
    vi.clearAllMocks();
    capturedOnLoad = undefined;
  });

  it('does not call fitBounds before the map has loaded', () => {
    const routeCoordinates = [
      { longitude: 1, latitude: 1 },
      { longitude: 2, latitude: 2 },
    ];
    render(<RouteMap startPoint={null} routeCoordinates={routeCoordinates} />);

    expect(mapMethods.fitBounds).not.toHaveBeenCalled();
  });

  it('calls resize then fitBounds once the map load event fires', () => {
    const routeCoordinates = [
      { longitude: 1, latitude: 1 },
      { longitude: 2, latitude: 2 },
    ];
    render(<RouteMap startPoint={null} routeCoordinates={routeCoordinates} />);

    act(() => {
      capturedOnLoad?.();
    });

    expect(mapMethods.resize).toHaveBeenCalled();
    expect(mapMethods.fitBounds).toHaveBeenCalledWith(
      [[1, 1], [2, 2]],
      expect.objectContaining({ padding: 50 }),
    );
  });

  it('does not call flyTo before the map has loaded', () => {
    render(<RouteMap startPoint={{ lon: 5, lat: 6 }} routeCoordinates={null} />);

    expect(mapMethods.flyTo).not.toHaveBeenCalled();
  });

  it('calls resize then flyTo once the map load event fires', () => {
    render(<RouteMap startPoint={{ lon: 5, lat: 6 }} routeCoordinates={null} />);

    act(() => {
      capturedOnLoad?.();
    });

    expect(mapMethods.resize).toHaveBeenCalled();
    expect(mapMethods.flyTo).toHaveBeenCalledWith(
      expect.objectContaining({ center: [5, 6] }),
    );
  });
});
