import { Copy, Check } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useCopyToClipboard } from '@/components/tool-ui/shared/use-copy-to-clipboard'

interface IssueIdStripProps {
  issueIds: string[]
}

export function IssueIdStrip({ issueIds }: IssueIdStripProps) {
  const { copiedId, copy } = useCopyToClipboard()

  if (issueIds.length === 0) return null

  return (
    <div className="flex flex-wrap gap-1.5">
      {issueIds.map((id) => (
        <span
          key={id}
          className="flex items-center gap-1 rounded-md border px-2 py-0.5 font-mono text-sm"
        >
          {id}
          <Button
            size="sm"
            variant="ghost"
            className="h-5 w-5 p-0"
            onClick={() => copy(id, id)}
            aria-label={copiedId === id ? `Copied ${id}` : `Copy ${id}`}
          >
            {copiedId === id ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
          </Button>
        </span>
      ))}
    </div>
  )
}
