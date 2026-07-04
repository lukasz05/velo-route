import { render, screen } from '@testing-library/react'
import { describe, it, expect } from 'vitest'
import ErrorMessage from './ErrorMessage'
import { RouteGenerationError } from '@/types/route'

describe('ErrorMessage', () => {
  it('renders nothing when error is null', () => {
    const { container } = render(<ErrorMessage error={null} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders known error codes with their message', () => {
    render(<ErrorMessage error={new RouteGenerationError('NO_ROUTE', 'no route')} />)
    expect(screen.getByRole('alert')).toHaveTextContent(
      'No road route found — try a different start point or adjust the distance range.'
    )
  })

  it('renders fallback message for unknown error codes', () => {
    render(<ErrorMessage error={new RouteGenerationError('UNKNOWN_CODE', 'unknown')} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong — please try again.')
  })
})
