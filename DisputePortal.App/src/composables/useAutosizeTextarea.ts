import { onUnmounted, watch, type Ref } from 'vue'
export function useAutosizeTextarea(
  target: Ref<HTMLTextAreaElement | null>,
  value: Ref<string>
): void {
  let observer: ResizeObserver | undefined,
    frame = 0,
    width = 0
  function resize() {
    cancelAnimationFrame(frame)
    frame = requestAnimationFrame(() => {
      const input = target.value
      if (!input) return
      const panel = input.closest<HTMLElement>('.chat-panel')
      const form = input.closest('form')
      const fixed = panel
        ? Array.from(panel.children)
            .filter((element) => element !== form && !element.classList.contains('chat-messages'))
            .reduce((height, element) => height + element.getBoundingClientRect().height, 0)
        : 0
      const room = panel && panel.clientHeight ? panel.clientHeight - fixed - 104 : 320
      const limit = Math.max(
        48,
        Math.min(320, (window.visualViewport?.height ?? window.innerHeight) * 0.42, room)
      )
      input.style.height = 'auto'
      const height = input.scrollHeight
      input.style.height = Math.min(limit, Math.max(48, height + 2)) + 'px'
      input.style.overflowY = height > limit ? 'auto' : 'hidden'
    })
  }
  watch(
    [target, value],
    () => {
      resize()
    },
    { flush: 'post' }
  )
  watch(
    target,
    (input) => {
      observer?.disconnect()
      width = 0
      if (input && typeof ResizeObserver !== 'undefined') {
        observer = new ResizeObserver((entries) => {
          const next = entries[0]?.contentRect.width ?? 0
          if (next !== width) {
            width = next
            resize()
          }
        })
        observer.observe(input)
      }
    },
    { flush: 'post' }
  )
  window.addEventListener('resize', resize)
  window.visualViewport?.addEventListener('resize', resize)
  onUnmounted(() => {
    cancelAnimationFrame(frame)
    observer?.disconnect()
    window.removeEventListener('resize', resize)
    window.visualViewport?.removeEventListener('resize', resize)
  })
}
