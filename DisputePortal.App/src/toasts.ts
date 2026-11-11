import { readonly, ref } from 'vue'
export type ToastKind = 'success' | 'error' | 'info'
export interface Toast {
  id: number
  kind: ToastKind
  message: string
}
const items = ref<Toast[]>([])
let sequence = 0
const timers = new Map<number, ReturnType<typeof setTimeout>>()
export const toastRevision = ref(0)
export const toasts = readonly(items)
export function dismissToast(id: number) {
  const timer = timers.get(id)
  if (timer) clearTimeout(timer)
  timers.delete(id)
  items.value = items.value.filter((t) => t.id !== id)
}
export function pauseToast(id: number) {
  const timer = timers.get(id)
  if (timer) clearTimeout(timer)
  timers.delete(id)
}
export function resumeToast(id: number) {
  pauseToast(id)
  const item = items.value.find((t) => t.id === id)
  if (item)
    timers.set(
      id,
      setTimeout(() => dismissToast(id), item.kind === 'error' ? 14000 : 7000)
    )
}
export function notify(message: string, kind: ToastKind = 'info') {
  const clean = message.trim()
  if (!clean) return
  toastRevision.value++
  const previous = items.value.find((t) => t.message === clean)
  if (previous) {
    resumeToast(previous.id)
    return
  }
  const item = { id: ++sequence, message: clean, kind }
  items.value.push(item)
  if (items.value.length > 4) dismissToast(items.value[0]!.id)
  resumeToast(item.id)
}
