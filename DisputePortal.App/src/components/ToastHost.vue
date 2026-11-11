<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { CircleCheck, CircleAlert, Info, X } from '@lucide/vue'
import { toasts, toastRevision, dismissToast, pauseToast, resumeToast } from '../toasts'
const host = ref<HTMLElement | null>(null)
const target = ref<HTMLElement | string>('body')
let observer: MutationObserver | undefined,
  revision = 0,
  disposed = false
async function present() {
  const current = ++revision
  // A modal makes nodes outside it inert. Keep notifications inside the active
  // dialog so their dismiss buttons remain usable, then raise the native popover.
  const dialogs = Array.from(document.querySelectorAll<HTMLDialogElement>('dialog[open]'))
  target.value = dialogs.at(-1) ?? 'body'
  await nextTick()
  if (disposed || current !== revision || !host.value) return
  try {
    if (host.value.matches(':popover-open')) host.value.hidePopover?.()
    if (toasts.value.length) host.value.showPopover?.()
  } catch {
    // W3.CSS notifications remain readable where the Popover API is unavailable.
  }
}
watch(
  () => [toasts.value.length, toastRevision.value],
  () => void present(),
  { flush: 'post', immediate: true }
)
onMounted(() => {
  observer = new MutationObserver((records) => {
    if (
      records.some(
        (record) =>
          (record.type === 'attributes' && record.target instanceof HTMLDialogElement) ||
          (record.type === 'childList' &&
            [...record.addedNodes, ...record.removedNodes].some(
              (node) =>
                node instanceof Element &&
                (node.matches('dialog') || !!node.querySelector('dialog'))
            ))
      )
    )
      void present()
  })
  observer.observe(document.body, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ['open']
  })
  void present()
})
onUnmounted(() => {
  disposed = true
  revision++
  observer?.disconnect()
})
</script>
<template>
  <Teleport :to="target">
    <div
      v-if="toasts.length"
      ref="host"
      popover="manual"
      class="toast-host w3-col s11 m6 l4 w3-border-0 w3-padding-small w3-margin-top w3-margin-right w3-transparent"
      aria-label="Notifications"
    >
      <div class="w3-bar">
        <section
          v-for="item in toasts"
          :key="item.id"
          class="app-toast w3-white w3-text-black w3-card w3-border w3-leftbar w3-round-large w3-container w3-padding-16 w3-margin-bottom"
          :class="
            item.kind === 'error'
              ? 'toast-error w3-border-red'
              : item.kind === 'success'
                ? 'toast-success w3-border-teal'
                : 'toast-info w3-border-blue'
          "
          :role="item.kind === 'error' ? 'alert' : 'status'"
          :aria-live="item.kind === 'error' ? 'assertive' : 'polite'"
          aria-atomic="true"
          @mouseenter="pauseToast(item.id)"
          @mouseleave="resumeToast(item.id)"
          @focusin="pauseToast(item.id)"
          @focusout="resumeToast(item.id)"
        >
          <div class="w3-row">
            <div class="toast-symbol w3-col s1">
              <CircleCheck
                v-if="item.kind === 'success'"
                :size="21"
              />
              <CircleAlert
                v-else-if="item.kind === 'error'"
                :size="21"
              />
              <Info
                v-else
                :size="21"
              />
            </div>
            <div class="w3-col s9 w3-container">
              <strong>
                {{
                  item.kind === 'error'
                    ? 'Please Check'
                    : item.kind === 'success'
                      ? 'Done'
                      : 'Update'
                }}
              </strong>
              <p>{{ item.message }}</p>
            </div>
            <button
              type="button"
              class="toast-close w3-col s2 w3-button w3-padding-16 w3-round-large"
              aria-label="Dismiss Notification"
              @click="dismissToast(item.id)"
            >
              <X :size="18" />
            </button>
          </div>
        </section>
      </div>
    </div>
  </Teleport>
</template>
