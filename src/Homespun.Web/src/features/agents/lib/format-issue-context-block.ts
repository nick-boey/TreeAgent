import type { IssueResponse } from '@/api/generated/types.gen'

export function formatIssueContextBlock(issue: IssueResponse): string {
  const lines: string[] = []
  if (issue.id) lines.push(`Issue ID: ${issue.id}`)
  if (issue.title) lines.push(`Title: ${issue.title}`)
  if (issue.description) lines.push(`Description: ${issue.description}`)
  return lines.join('\n')
}
