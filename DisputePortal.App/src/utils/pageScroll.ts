interface PagePosition {
  left: number
  top: number
}
const positions = new Map<string, PagePosition>()
const maximumEntries = 60
let cancelRestoration: (() => void) | undefined
function scrollingPage(): HTMLElement | null {
  if (
    typeof window === 'undefined' ||
    typeof window.matchMedia !== 'function' ||
    !window.matchMedia('(max-width: 860px)').matches
  )
    return null
  return document.querySelector<HTMLElement>('.portal-layout > .main-column')
}
export function rememberPageScroll(path: string): void {
  cancelRestoration?.()
  const page = scrollingPage()
  if (!page) return
  positions.delete(path)
  positions.set(path, { left: page.scrollLeft, top: page.scrollTop })
  const oldest = positions.keys().next().value
  if (positions.size > maximumEntries && oldest !== undefined) positions.delete(oldest)
}
export function restorePageScroll(path: string, saved: PagePosition | null): PagePosition | false {
  cancelRestoration?.()
  const page = scrollingPage()
  if (!page) return saved ?? { left: 0, top: 0 }
  const position = saved ? (positions.get(path) ?? saved) : { left: 0, top: 0 }
  page.scrollTo({ ...position, behavior: 'instant' })
  // Back navigation may arrive before an asynchronous list has rendered.
  const content = page.querySelector<HTMLElement>('.content-area')
  if (
    saved &&
    content &&
    typeof ResizeObserver !== 'undefined' &&
    page.scrollHeight - page.clientHeight < position.top
  ) {
    const stop = () => {
      observer.disconnect()
      clearTimeout(timeout)
      for (const event of ['pointerdown', 'touchstart', 'wheel', 'keydown'])
        page.removeEventListener(event, stop)
      if (cancelRestoration === stop) cancelRestoration = undefined
    }
    const observer = new ResizeObserver(() => {
      if (!page.isConnected) {
        stop()
        return
      }
      page.scrollTo({ ...position, behavior: 'instant' })
      if (page.scrollHeight - page.clientHeight >= position.top) stop()
    })
    const timeout = setTimeout(stop, 5000)
    for (const event of ['pointerdown', 'touchstart', 'wheel', 'keydown'])
      page.addEventListener(event, stop, { passive: true })
    cancelRestoration = stop
    observer.observe(content)
  }
  return false
}
