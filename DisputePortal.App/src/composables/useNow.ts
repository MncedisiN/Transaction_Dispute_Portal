import { onMounted, onUnmounted, ref } from 'vue'
const now = ref(Date.now())
let users = 0
let timer: ReturnType<typeof setInterval> | undefined
export function useNow() {
  onMounted(() => {
    if (users++ === 0) {
      now.value = Date.now()
      timer = setInterval(() => {
        now.value = Date.now()
      }, 30000)
    }
  })
  onUnmounted(() => {
    if (--users === 0) {
      clearInterval(timer)
      timer = undefined
    }
  })
  return now
}
