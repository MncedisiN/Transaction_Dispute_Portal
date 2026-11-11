export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard && window.isSecureContext) {
    await navigator.clipboard.writeText(text)
    return
  }
  const focused = document.activeElement instanceof HTMLElement ? document.activeElement : null
  const left = window.scrollX,
    top = window.scrollY
  const input = document.createElement('textarea')
  input.value = text
  input.setAttribute('readonly', '')
  input.style.position = 'fixed'
  input.style.top = '0'
  input.style.left = '0'
  input.style.width = '1px'
  input.style.height = '1px'
  input.style.opacity = '0'
  input.style.fontSize = '16px'
  input.style.pointerEvents = 'none'
  document.body.appendChild(input)
  try {
    input.focus({ preventScroll: true })
    input.select()
    input.setSelectionRange(0, text.length)
    if (!document.execCommand('copy')) throw new Error('Copy unavailable')
  } finally {
    input.remove()
    focused?.focus({ preventScroll: true })
    if (window.scrollX !== left || window.scrollY !== top) window.scrollTo(left, top)
  }
}
