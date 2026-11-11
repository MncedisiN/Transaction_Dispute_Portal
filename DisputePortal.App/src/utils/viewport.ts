// One viewport owner for ordinary forms, native dialogs and full-screen chat.
export function trackVisibleViewport(): () => void {
  const viewport = window.visualViewport
  let frame = 0
  let restingHeight = Math.max(window.innerHeight, viewport?.height ?? 0)
  let disposed = false

  function keepFocusedControlVisible(top: number, height: number) {
    const active = document.activeElement
    if (
      !(active instanceof HTMLElement) ||
      !active.matches('input,textarea,select,[contenteditable="true"]')
    )
      return
    const modal = active.closest('dialog[open]')
    let container = active.parentElement
    while (container && container !== document.body) {
      if (
        /auto|scroll/.test(getComputedStyle(container).overflowY) &&
        container.scrollHeight > container.clientHeight + 1
      )
        break
      container = container.parentElement
    }
    const header =
      !modal && !document.documentElement.hasAttribute('data-keyboard-open')
        ? active.closest('.portal-layout')?.querySelector('.topbar')?.getBoundingClientRect()
        : undefined
    if (!container || container === document.body) {
      if (modal) return
      const upper = Math.max(top + 12, header?.bottom ?? 0)
      const lower = top + height - 12
      const control = active.getBoundingClientRect()
      if (lower <= upper) return
      if (control.height > lower - upper) {
        if (control.top < upper || control.top > lower - 44) window.scrollBy(0, control.top - upper)
      } else if (control.bottom > lower) window.scrollBy(0, control.bottom - lower)
      else if (control.top < upper) window.scrollBy(0, control.top - upper)
      return
    }
    const bounds = container.getBoundingClientRect()
    const upper = Math.max(top + 12, bounds.top + 12, header?.bottom ?? 0)
    const lower = Math.min(top + height - 12, bounds.bottom - 12)
    const control = active.getBoundingClientRect()
    if (lower <= upper) return
    // Scroll the existing form container once, rather than moving the page or
    // changing focus on every keystroke. Textareas retain their own caret scroll.
    const visibleHeight = lower - upper
    if (control.height > visibleHeight) {
      if (control.top < upper || control.top > lower - 44)
        container.scrollTop += control.top - upper
    } else if (control.bottom > lower) container.scrollTop += control.bottom - lower
    else if (control.top < upper) container.scrollTop -= upper - control.top
  }

  function measure() {
    if (disposed || (viewport && Math.abs(viewport.scale - 1) > 0.01)) return
    const height = viewport?.height ?? window.innerHeight
    const top = viewport?.offsetTop ?? 0
    document.documentElement.style.setProperty('--portal-viewport-height', height + 'px')
    document.documentElement.style.setProperty('--portal-viewport-top', top + 'px')
    const editable =
      document.activeElement?.matches('input,textarea,select,[contenteditable="true"]') ?? false
    if (!editable) restingHeight = Math.max(window.innerHeight, height)
    const keyboard = editable && restingHeight - height > 120
    document.documentElement.toggleAttribute('data-keyboard-open', keyboard)
    if (keyboard) keepFocusedControlVisible(top, height)
  }

  function schedule() {
    cancelAnimationFrame(frame)
    frame = requestAnimationFrame(measure)
  }
  measure()
  viewport?.addEventListener('resize', schedule)
  viewport?.addEventListener('scroll', schedule)
  window.addEventListener('resize', schedule)
  document.addEventListener('focusin', schedule)
  document.addEventListener('focusout', schedule)
  return () => {
    disposed = true
    document.documentElement.removeAttribute('data-keyboard-open')
    document.documentElement.style.removeProperty('--portal-viewport-height')
    document.documentElement.style.removeProperty('--portal-viewport-top')
    cancelAnimationFrame(frame)
    viewport?.removeEventListener('resize', schedule)
    viewport?.removeEventListener('scroll', schedule)
    window.removeEventListener('resize', schedule)
    document.removeEventListener('focusin', schedule)
    document.removeEventListener('focusout', schedule)
  }
}
