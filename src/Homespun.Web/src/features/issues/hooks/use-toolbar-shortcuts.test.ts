import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useToolbarShortcuts, type ToolbarShortcutCallbacks } from './use-toolbar-shortcuts'

describe('useToolbarShortcuts', () => {
  let callbacks: ToolbarShortcutCallbacks

  beforeEach(() => {
    callbacks = {
      onCreateAbove: vi.fn(),
      onCreateBelow: vi.fn(),
      onOpenAgentLauncher: vi.fn(),
      onDecreaseDepth: vi.fn(),
      onIncreaseDepth: vi.fn(),
      onFocusSearch: vi.fn(),
      onNextMatch: vi.fn(),
      onPreviousMatch: vi.fn(),
      onEmbedSearch: vi.fn(),
    }
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  function dispatchKeyDown(key: string, options: Partial<KeyboardEvent> = {}) {
    const event = new KeyboardEvent('keydown', {
      key,
      bubbles: true,
      ...options,
    })
    document.dispatchEvent(event)
  }

  it('calls onCreateAbove when Shift+O is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('O', { shiftKey: true })
    expect(callbacks.onCreateAbove).toHaveBeenCalled()
  })

  it('calls onCreateBelow when O is pressed (without shift)', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('o')
    expect(callbacks.onCreateBelow).toHaveBeenCalled()
  })

  it('calls onOpenAgentLauncher when e is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('e')
    expect(callbacks.onOpenAgentLauncher).toHaveBeenCalled()
  })

  it('calls onDecreaseDepth when [ is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('[')
    expect(callbacks.onDecreaseDepth).toHaveBeenCalled()
  })

  it('calls onIncreaseDepth when ] is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown(']')
    expect(callbacks.onIncreaseDepth).toHaveBeenCalled()
  })

  it('calls onFocusSearch when / is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('/')
    expect(callbacks.onFocusSearch).toHaveBeenCalled()
  })

  it('calls onNextMatch when n is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('n')
    expect(callbacks.onNextMatch).toHaveBeenCalled()
  })

  it('calls onPreviousMatch when N (Shift+n) is pressed', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    dispatchKeyDown('N', { shiftKey: true })
    expect(callbacks.onPreviousMatch).toHaveBeenCalled()
  })

  it('does not trigger shortcuts when typing in input fields', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()

    // Create and dispatch event from input element
    const event = new KeyboardEvent('keydown', {
      key: 'o',
      bubbles: true,
    })
    Object.defineProperty(event, 'target', { value: input })
    document.dispatchEvent(event)

    expect(callbacks.onCreateBelow).not.toHaveBeenCalled()

    document.body.removeChild(input)
  })

  it('does not trigger shortcuts when typing in textarea', () => {
    renderHook(() => useToolbarShortcuts(callbacks))

    const textarea = document.createElement('textarea')
    document.body.appendChild(textarea)
    textarea.focus()

    const event = new KeyboardEvent('keydown', {
      key: 'o',
      bubbles: true,
    })
    Object.defineProperty(event, 'target', { value: textarea })
    document.dispatchEvent(event)

    expect(callbacks.onCreateBelow).not.toHaveBeenCalled()

    document.body.removeChild(textarea)
  })

  it('cleans up event listener on unmount', () => {
    const removeEventListenerSpy = vi.spyOn(document, 'removeEventListener')
    const { unmount } = renderHook(() => useToolbarShortcuts(callbacks))

    unmount()

    expect(removeEventListenerSpy).toHaveBeenCalledWith('keydown', expect.any(Function))
    removeEventListenerSpy.mockRestore()
  })

  it('calls onToggleFilter when f is pressed', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f')
    expect(onToggleFilter).toHaveBeenCalled()
  })

  it('does not call onToggleFilter when f is pressed with shift', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f', { shiftKey: true })
    expect(onToggleFilter).not.toHaveBeenCalled()
  })

  it('does not call onToggleFilter when f is pressed with ctrl', () => {
    const onToggleFilter = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onToggleFilter }))

    dispatchKeyDown('f', { ctrlKey: true })
    expect(onToggleFilter).not.toHaveBeenCalled()
  })

  it('does nothing when onToggleFilter is undefined and f is pressed', () => {
    // Just ensure it doesn't throw
    renderHook(() => useToolbarShortcuts(callbacks))
    expect(() => dispatchKeyDown('f')).not.toThrow()
  })

  it('calls onFocusFilterAtEnd when f is pressed and filter is already active', () => {
    const onToggleFilter = vi.fn()
    const onFocusFilterAtEnd = vi.fn()
    renderHook(() =>
      useToolbarShortcuts({
        ...callbacks,
        onToggleFilter,
        isFilterActive: true,
        onFocusFilterAtEnd,
      })
    )

    dispatchKeyDown('f')
    expect(onFocusFilterAtEnd).toHaveBeenCalled()
    expect(onToggleFilter).not.toHaveBeenCalled()
  })

  it('calls onToggleFilter when f is pressed and filter is not active', () => {
    const onToggleFilter = vi.fn()
    const onFocusFilterAtEnd = vi.fn()
    renderHook(() =>
      useToolbarShortcuts({
        ...callbacks,
        onToggleFilter,
        isFilterActive: false,
        onFocusFilterAtEnd,
      })
    )

    dispatchKeyDown('f')
    expect(onToggleFilter).toHaveBeenCalled()
    expect(onFocusFilterAtEnd).not.toHaveBeenCalled()
  })

  it('calls onMoveUp when Ctrl+Shift+ArrowUp is pressed', () => {
    const onMoveUp = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onMoveUp, canMoveUp: true }))

    dispatchKeyDown('ArrowUp', { ctrlKey: true, shiftKey: true })
    expect(onMoveUp).toHaveBeenCalled()
  })

  it('calls onMoveDown when Ctrl+Shift+ArrowDown is pressed', () => {
    const onMoveDown = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onMoveDown, canMoveDown: true }))

    dispatchKeyDown('ArrowDown', { ctrlKey: true, shiftKey: true })
    expect(onMoveDown).toHaveBeenCalled()
  })

  it('does not call onMoveUp when bare k is pressed', () => {
    const onMoveUp = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onMoveUp, canMoveUp: true }))

    dispatchKeyDown('k')
    expect(onMoveUp).not.toHaveBeenCalled()
  })

  it('does not call onMoveDown when bare j is pressed', () => {
    const onMoveDown = vi.fn()
    renderHook(() => useToolbarShortcuts({ ...callbacks, onMoveDown, canMoveDown: true }))

    dispatchKeyDown('j')
    expect(onMoveDown).not.toHaveBeenCalled()
  })

  it('falls back to onToggleFilter when filter is active but onFocusFilterAtEnd is not provided', () => {
    const onToggleFilter = vi.fn()
    renderHook(() =>
      useToolbarShortcuts({
        ...callbacks,
        onToggleFilter,
        isFilterActive: true,
        // onFocusFilterAtEnd not provided
      })
    )

    dispatchKeyDown('f')
    expect(onToggleFilter).toHaveBeenCalled()
  })
})
