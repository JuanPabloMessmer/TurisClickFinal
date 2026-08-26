import { Spinner } from '@/components/ui/spinner'

export function FullScreenSpinner() {
  return (
    <div className="flex h-screen w-screen items-center justify-center bg-background">
      <Spinner className="h-8 w-8 border-[3px]" />
    </div>
  )
}
