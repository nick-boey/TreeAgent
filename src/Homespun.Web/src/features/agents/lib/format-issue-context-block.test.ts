import { describe, it, expect } from 'vitest'
import { formatIssueContextBlock } from './format-issue-context-block'

describe('formatIssueContextBlock', () => {
  it('includes all fields when present', () => {
    const result = formatIssueContextBlock({
      id: 'rkh0cc',
      title: 'My Issue',
      description: 'Some description',
    })
    expect(result).toBe('Issue ID: rkh0cc\nTitle: My Issue\nDescription: Some description')
  })

  it('omits description line when absent', () => {
    const result = formatIssueContextBlock({
      id: 'rkh0cc',
      title: 'My Issue',
    })
    expect(result).toBe('Issue ID: rkh0cc\nTitle: My Issue')
  })

  it('omits title line when absent (defensive)', () => {
    const result = formatIssueContextBlock({
      id: 'rkh0cc',
    })
    expect(result).toBe('Issue ID: rkh0cc')
  })

  it('does not produce trailing whitespace', () => {
    const result = formatIssueContextBlock({
      id: 'rkh0cc',
      title: 'My Issue',
      description: 'Some description',
    })
    expect(result).not.toMatch(/\s+$/)
  })
})
