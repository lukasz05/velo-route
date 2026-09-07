import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import RouteInfoPanel from './RouteInfoPanel'
import type { RouteResult } from '@/types/route'

vi.mock('@clerk/nextjs', () => ({
  useAuth: () => ({ getToken: vi.fn() }),
  useUser: () => ({ isSignedIn: false }),
}))

function makeRoute(overrides: Partial<RouteResult> = {}): RouteResult {
  return {
    geometry: { coordinates: [{ longitude: 0, latitude: 0 }] },
    distanceMeters: 30000,
    segments: [],
    pavedRatio: 0.8,
    smoothnessScore: 0.9,
    overlapRatio: 0.1,
    qualityWarning: false,
    maxConsecutiveSharpTurns: 0,
    ...overrides,
  }
}

describe('RouteInfoPanel', () => {
  it('shows no quality notice when qualityWarning is false', () => {
    render(<RouteInfoPanel route={makeRoute({ qualityWarning: false })} />)
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('shows a non-blocking quality notice when qualityWarning is true', () => {
    render(<RouteInfoPanel route={makeRoute({ qualityWarning: true })} />)
    expect(screen.getByRole('status')).toHaveTextContent(/overlap|backtracking/i)
  })
})
