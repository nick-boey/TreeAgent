import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, act, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IssueIdStrip } from './issue-id-strip'

describe('IssueIdStrip', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders nothing for empty input', () => {
    const { container } = render(<IssueIdStrip issueIds={[]} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders one chip per id', () => {
    render(<IssueIdStrip issueIds={['rkh0cc', 'abc123']} />)
    expect(screen.getByText('rkh0cc')).toBeInTheDocument()
    expect(screen.getByText('abc123')).toBeInTheDocument()
    expect(screen.getAllByRole('button')).toHaveLength(2)
  })

  it('clicking the copy button writes the id to the clipboard', async () => {
    // userEvent.setup() installs its own clipboard mock — spy on it after setup
    const user = userEvent.setup()
    const writeTextSpy = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue()

    render(<IssueIdStrip issueIds={['rkh0cc']} />)

    await user.click(screen.getByRole('button', { name: /copy rkh0cc/i }))

    expect(writeTextSpy).toHaveBeenCalledWith('rkh0cc')
  })

  it('icon switches to Check after click then back to Copy after ~2s', async () => {
    vi.useFakeTimers()
    vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue()

    render(<IssueIdStrip issueIds={['rkh0cc']} />)

    // Before click: Copy button visible
    expect(screen.getByRole('button', { name: /^copy rkh0cc$/i })).toBeInTheDocument()

    // Fire the click — starts async clipboard write
    fireEvent.click(screen.getByRole('button', { name: /^copy rkh0cc$/i }))
    // Flush clipboard Promise microtask so setCopiedId is called
    await act(async () => {})

    // After click: aria-label changes to "Copied" (Check icon)
    expect(screen.getByRole('button', { name: /^copied rkh0cc$/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^copy rkh0cc$/i })).not.toBeInTheDocument()

    // After 2s: Copy icon returns
    act(() => {
      vi.advanceTimersByTime(2100)
    })

    expect(screen.getByRole('button', { name: /^copy rkh0cc$/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^copied rkh0cc$/i })).not.toBeInTheDocument()

    vi.useRealTimers()
  })
})
