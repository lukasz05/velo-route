import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import ConfirmModal from './ConfirmModal'

describe('ConfirmModal', () => {
  it('renders title and message', () => {
    render(
      <ConfirmModal
        title="Delete route?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={() => {}}
        onCancel={() => {}}
      />
    )
    expect(screen.getByText('Delete route?')).toBeInTheDocument()
    expect(screen.getByText('This cannot be undone.')).toBeInTheDocument()
  })

  it('calls onCancel when Cancel is clicked', () => {
    const onCancel = vi.fn()
    render(
      <ConfirmModal
        title="Delete route?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={() => {}}
        onCancel={onCancel}
      />
    )
    fireEvent.click(screen.getByText('Cancel'))
    expect(onCancel).toHaveBeenCalledOnce()
  })

  it('calls onConfirm when the confirm button is clicked', () => {
    const onConfirm = vi.fn()
    render(
      <ConfirmModal
        title="Delete route?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={onConfirm}
        onCancel={() => {}}
      />
    )
    fireEvent.click(screen.getByText('Delete'))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disables the confirm button while isConfirming is true', () => {
    render(
      <ConfirmModal
        title="Delete route?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={() => {}}
        onCancel={() => {}}
        isConfirming
      />
    )
    expect(screen.getByText('Deleting…')).toBeDisabled()
  })

  it('keeps confirm disabled until the typed value matches confirmationPhrase', () => {
    render(
      <ConfirmModal
        title="Delete account?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={() => {}}
        onCancel={() => {}}
        confirmationPhrase="DELETE"
      />
    )
    const confirmButton = screen.getByText('Delete')
    const input = screen.getByLabelText('Type DELETE to confirm')

    expect(confirmButton).toBeDisabled()

    fireEvent.change(input, { target: { value: 'delete' } })
    expect(confirmButton).toBeDisabled()

    fireEvent.change(input, { target: { value: 'DELETE' } })
    expect(confirmButton).not.toBeDisabled()
  })

  it('is unaffected when confirmationPhrase is omitted', () => {
    const onConfirm = vi.fn()
    render(
      <ConfirmModal
        title="Delete route?"
        message="This cannot be undone."
        confirmLabel="Delete"
        onConfirm={onConfirm}
        onCancel={() => {}}
      />
    )
    const confirmButton = screen.getByText('Delete')
    expect(confirmButton).not.toBeDisabled()
    fireEvent.click(confirmButton)
    expect(onConfirm).toHaveBeenCalledOnce()
  })
})
