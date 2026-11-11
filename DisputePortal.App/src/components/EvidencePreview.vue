<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { decodeEvidenceImage } from '../utils/prepareEvidence'
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
import { ChevronLeft, ChevronRight, Download, X } from '@lucide/vue'
import type { PDFDocumentLoadingTask, PDFDocumentProxy, RenderTask } from 'pdfjs-dist'
import { api, ApiError } from '../api'
import { evidenceCategoryLabel, type EvidenceFile } from '../utils/evidence'
const props = defineProps<{ caseId: string; file: EvidenceFile }>()
const emit = defineEmits<{ close: [] }>()
const dialog = ref<HTMLDialogElement>(),
  canvas = ref<HTMLCanvasElement>()
const error = ref(''),
  busy = ref(true),
  ready = ref(false),
  downloading = ref(false),
  pageNumber = ref(1),
  pages = ref(1)
const request = new AbortController()
const base = '/api/v1/disputes/' + props.caseId + '/evidence/' + props.file.id
let task: PDFDocumentLoadingTask | undefined,
  pdf: PDFDocumentProxy | undefined,
  render: RenderTask | undefined,
  disposed = false
const urls = new Set<string>()
async function drawPage(number: number) {
  if (!pdf || !canvas.value || disposed) return
  busy.value = true
  error.value = ''
  try {
    const page = await pdf.getPage(number)
    if (disposed || !canvas.value) return
    const original = page.getViewport({ scale: 1 })
    const scale = Math.min(2, 1600 / Math.max(original.width, original.height))
    const viewport = page.getViewport({ scale })
    canvas.value.width = Math.ceil(viewport.width)
    canvas.value.height = Math.ceil(viewport.height)
    const context = canvas.value.getContext('2d')
    if (!context) throw new Error('Canvas unavailable')
    render = page.render({ canvas: canvas.value, canvasContext: context, viewport })
    await render.promise
    if (!disposed) {
      pageNumber.value = number
      ready.value = true
    }
    page.cleanup()
  } catch {
    if (!disposed) error.value = 'This page could not be displayed.'
  } finally {
    if (!disposed) busy.value = false
  }
}
async function download(original = false) {
  if (!ready.value || downloading.value) return
  downloading.value = true
  error.value = ''
  try {
    // Re-authorize and re-scan on download; never reuse preview bytes as an authorization shortcut.
    const blob = await api.download(base + (original ? '/original' : ''), request.signal)
    if (disposed) return
    const url = URL.createObjectURL(blob)
    urls.add(url)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = original
      ? (props.file.originalFileName ?? 'original-photo')
      : props.file.fileName
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    setTimeout(() => {
      URL.revokeObjectURL(url)
      urls.delete(url)
    }, 1000)
  } catch (e) {
    if (!disposed)
      error.value = e instanceof ApiError ? e.message : 'Download unavailable. Please retry.'
  } finally {
    if (!disposed) downloading.value = false
  }
}
onMounted(async () => {
  dialog.value?.showModal()
  try {
    const blob = await api.download(base + '/preview', request.signal)
    if (disposed) return
    await nextTick()
    if (blob.type === 'application/pdf') {
      const [library, worker] = await Promise.all([
        import('pdfjs-dist'),
        import('pdfjs-dist/build/pdf.worker.min.mjs?url')
      ])
      if (disposed) return
      library.GlobalWorkerOptions.workerSrc = worker.default
      const bytes = new Uint8Array(await blob.arrayBuffer())
      if (disposed) return
      task = library.getDocument({
        data: bytes,
        disableFontFace: true,
        useSystemFonts: true,
        enableXfa: false,
        stopAtErrors: true,
        maxImageSize: 16000000,
        canvasMaxAreaInBytes: 16000000,
        useWasm: false
      })
      pdf = await task.promise
      if (disposed) {
        await task.destroy()
        return
      }
      pages.value = pdf.numPages
      await drawPage(1)
    } else if (['image/png', 'image/jpeg', 'image/webp'].includes(blob.type)) {
      const bitmap = await decodeEvidenceImage(blob)
      try {
        if (disposed || !canvas.value) return
        const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height))
        canvas.value.width = Math.max(1, Math.round(bitmap.width * scale))
        canvas.value.height = Math.max(1, Math.round(bitmap.height * scale))
        const context = canvas.value.getContext('2d')
        if (!context) throw new Error('Canvas unavailable')
        context.drawImage(bitmap.source, 0, 0, canvas.value.width, canvas.value.height)
        ready.value = true
      } finally {
        bitmap.close()
      }
    } else {
      throw new Error('Unsupported preview')
    }
  } catch (e) {
    if (!disposed)
      error.value =
        e instanceof ApiError
          ? e.message
          : 'This document could not be previewed. Try a clear PDF, JPEG or PNG.'
  } finally {
    if (!disposed) busy.value = false
  }
})
onUnmounted(() => {
  disposed = true
  request.abort()
  render?.cancel()
  void task?.destroy().catch(() => {})
  for (const url of urls) URL.revokeObjectURL(url)
  urls.clear()
  if (canvas.value) {
    canvas.value.width = 0
    canvas.value.height = 0
  }
  dialog.value?.close()
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <Teleport to="body">
    <dialog
      ref="dialog"
      class="evidence-dialog evidence-preview w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m10 l8"
      aria-label="Document Preview"
      @close="emit('close')"
    >
      <div class="w3-bar">
        <header class="evidence-modal-header w3-bar w3-padding-16">
          <button
            class="icon-button w3-button w3-round-large w3-right"
            aria-label="Close Document Preview"
            @click="dialog?.close()"
          >
            <X :size="22" />
          </button>
          <div>
            <h2 class="w3-sans-serif w3-large">{{ evidenceCategoryLabel(file.category) }}</h2>
            <p>{{ file.fileName }}</p>
          </div>
        </header>
        <p
          v-if="error"
          role="alert"
          class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        >
          {{ error }}
        </p>
        <p
          v-if="busy"
          role="status"
        >
          Preparing preview…
        </p>
        <div class="evidence-canvas w3-center">
          <canvas
            ref="canvas"
            class="w3-image"
            :aria-label="file.fileName + ', page ' + pageNumber"
            role="img"
          />
        </div>
        <nav
          v-if="pages > 1"
          class="evidence-pages w3-bar w3-section w3-center"
          aria-label="Document Pages"
        >
          <button
            class="icon-button w3-button w3-round-large"
            aria-label="Previous Page"
            :disabled="busy || pageNumber <= 1"
            @click="drawPage(pageNumber - 1)"
          >
            <ChevronLeft :size="20" />
          </button>
          <span>{{ pageNumber }} / {{ pages }}</span>
          <button
            class="icon-button w3-button w3-round-large"
            aria-label="Next Page"
            :disabled="busy || pageNumber >= pages"
            @click="drawPage(pageNumber + 1)"
          >
            <ChevronRight :size="20" />
          </button>
        </nav>
        <button
          class="button button-primary evidence-upload w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
          :disabled="!ready || busy || downloading"
          @click="download()"
        >
          <Download
            class="w3-margin-right"
            :size="18"
          />
          {{ downloading ? 'Downloading…' : 'Download Document' }}
        </button>
        <button
          v-if="file.hasOriginal"
          class="button button-secondary evidence-upload w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="!ready || busy || downloading"
          @click="download(true)"
        >
          Download Retained Original
        </button>
      </div>
    </dialog>
  </Teleport>
</template>
