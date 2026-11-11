import { watch, type Ref } from 'vue'
import { notify } from '../toasts'
export function useFeedback(error?: Ref<string>, success?: Ref<string>, information?: Ref<string>) {
  if (error)
    watch(error, (value) => {
      if (value) notify(value, 'error')
    })
  if (success)
    watch(success, (value) => {
      if (value) notify(value, 'success')
    })
  if (information)
    watch(information, (value) => {
      if (value) notify(value, 'info')
    })
}
