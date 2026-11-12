<script setup lang="ts">
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import { prepareEvidence } from '../utils/prepareEvidence'
import { computed, defineAsyncComponent, onUnmounted, ref, watch } from 'vue'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { Paperclip, Camera, Eye, LockKeyhole, FileText, X, Trash2 } from '@lucide/vue'
import ChoiceSheet from './ChoiceSheet.vue'
import { evidenceCategories, evidenceCategoryLabel, type EvidenceFile } from '../utils/evidence'
const EvidencePreview = defineAsyncComponent(() => import('./EvidencePreview.vue'))
const EvidenceAccessPanel = defineAsyncComponent(() => import('./EvidenceAccessPanel.vue'))
const EvidenceRemovalDialog = defineAsyncComponent(() => import('./EvidenceRemovalDialog.vue'))
import { api, ApiError } from '../api'
import { formatDate } from '../utils/format'
const props = withDefaults(
  defineProps<{
    caseId: string
    canUpload?: boolean
    canManageAccess?: boolean
    status?: string
  }>(),
  { canUpload: true, canManageAccess: false, status: undefined }
)
const uploadAllowed = computed(
  () => props.canUpload && (reference.evidence?.uploadStatuses ?? []).includes(props.status ?? '')
)
const emit = defineEmits<{ changed: [] }>()
type Evidence = EvidenceFile
interface PendingFile {
  file: File
  original?: File
  preview?: string
  fingerprint: string
  category: string
  error: string
}
const preparing = ref(false),
  completed = ref(0),
  uploadTotal = ref(0),
  activeUpload = ref('')
const category = ref(''),
  preview = ref<Evidence | null>(null),
  access = ref<Evidence | null>(null)
const removal = ref<Evidence | null>(null)
const files = ref<Evidence[]>([]),
  pending = ref<PendingFile[]>([]),
  error = ref(''),
  notice = ref(''),
  loading = ref(false),
  uploading = ref(false)
const picker = ref<HTMLInputElement | null>(null),
  camera = ref<HTMLInputElement | null>(null)
let request: AbortController | undefined,
  mutation: AbortController | undefined,
  generation = 0,
  disposed = false
const size = (bytes: number) =>
  bytes < 1048576 ? Math.ceil(bytes / 1024) + ' KB' : (bytes / 1048576).toFixed(1) + ' MB'
async function load() {
  const current = ++generation
  request?.abort()
  request = new AbortController()
  loading.value = true
  try {
    const result = await api.get<Evidence[]>(
      '/api/v1/disputes/' + props.caseId + '/evidence',
      request.signal
    )
    if (current === generation) {
      files.value = result
      error.value = ''
    }
  } catch (e) {
    if (current === generation && !request.signal.aborted)
      error.value = e instanceof ApiError ? e.message : 'Evidence is temporarily unavailable.'
  } finally {
    if (current === generation) loading.value = false
  }
}
async function choose(event: Event) {
  const input = event.target as HTMLInputElement,
    selected = Array.from(input.files ?? []),
    owner = props.caseId
  input.value = ''
  error.value = ''
  notice.value = ''
  if (!uploadAllowed.value) return
  if (!category.value) {
    error.value = 'Choose a document category first.'
    return
  }
  preparing.value = true
  try {
    for (const source of selected) {
      if (files.value.length + pending.value.length >= (reference.evidence?.maximumFiles ?? 0)) {
        error.value = `Each case allows up to ${reference.evidence?.maximumFiles} files.`
        break
      }
      const fingerprint = source.name + ':' + source.size + ':' + source.lastModified
      if (pending.value.some((x) => x.fingerprint === fingerprint)) continue
      try {
        const result = await prepareEvidence(source)
        if (disposed || owner !== props.caseId || !uploadAllowed.value) break
        pending.value.push({
          ...result,
          fingerprint,
          category: category.value,
          error: '',
          preview: result.file.type.startsWith('image/')
            ? URL.createObjectURL(result.file)
            : undefined
        })
      } catch (e) {
        error.value = e instanceof Error ? e.message : 'This file could not be prepared.'
      }
    }
  } finally {
    preparing.value = false
  }
}
function removePending(item: PendingFile) {
  if (item.preview) URL.revokeObjectURL(item.preview)
  pending.value = pending.value.filter((x) => x !== item)
}
function clearPending() {
  for (const item of pending.value) if (item.preview) URL.revokeObjectURL(item.preview)
  pending.value = []
}
async function upload() {
  if (!uploadAllowed.value || uploading.value || !pending.value.length) return
  uploading.value = true
  error.value = ''
  notice.value = ''
  mutation = new AbortController()
  const caseId = props.caseId
  let uploaded = 0
  completed.value = 0
  uploadTotal.value = pending.value.length
  try {
    for (const item of [...pending.value]) {
      if (disposed || caseId !== props.caseId || mutation.signal.aborted || !uploadAllowed.value)
        break
      item.error = ''
      activeUpload.value = item.file.name
      try {
        if (item.original)
          await api.upload(
            '/api/v1/disputes/' + caseId + '/evidence',
            item.file,
            item.category,
            mutation.signal,
            item.original
          )
        else
          await api.upload(
            '/api/v1/disputes/' + caseId + '/evidence',
            item.file,
            item.category,
            mutation.signal
          )
        removePending(item)
        uploaded++
        completed.value++
      } catch (e) {
        if (!mutation.signal.aborted)
          item.error = e instanceof ApiError ? e.message : 'Upload interrupted. Retry this file.'
      }
    }
    if (!disposed && caseId === props.caseId) {
      if (uploaded) {
        notice.value = uploaded === 1 ? 'Evidence added.' : uploaded + ' files added.'
        await load()
        emit('changed')
      }
    }
  } finally {
    if (!disposed) uploading.value = false
  }
}
watch(
  () => props.caseId,
  () => {
    mutation?.abort()
    preview.value = null
    access.value = null
    removal.value = null
    clearPending()
    files.value = []
    notice.value = ''
    void load()
  }
)
watch(
  () => props.canManageAccess,
  (allowed) => {
    if (!allowed) access.value = null
  }
)
watch(uploadAllowed, (allowed) => {
  if (!allowed) {
    mutation?.abort()
    clearPending()
  }
})
useLiveRefresh(() => load(), ['disputes'])
onUnmounted(() => {
  disposed = true
  generation++
  request?.abort()
  mutation?.abort()
  preview.value = null
  access.value = null
  removal.value = null
  clearPending()
})
function evidenceRemoved() {
  removal.value = null
  notice.value = 'File removed.'
  void load()
  emit('changed')
}
useFeedback(error, undefined, notice)
</script>
<template>
  <section
    class="surface-panel evidence-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
    aria-label="Case Evidence"
  >
    <div class="evidence-heading w3-bar w3-padding-16">
      <h2 class="w3-sans-serif w3-large">Evidence</h2>
      <span class="w3-tag w3-light-grey w3-round-large w3-small">
        {{ files.length }} / {{ reference.evidence?.maximumFiles }}
      </span>
    </div>
    <p
      v-if="error"
      role="alert"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
      <button
        class="text-button w3-button w3-text-black w3-round-large"
        @click="load"
      >
        Refresh
      </button>
    </p>
    <p
      v-if="notice"
      role="status"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
    >
      {{ notice }}
    </p>
    <template v-if="uploadAllowed">
      <div class="evidence-category w3-section">
        <ChoiceSheet
          v-model="category"
          label="Document Category"
          :options="evidenceCategories"
          :disabled="uploading || preparing"
        />
      </div>
      <input
        ref="picker"
        class="sr-only w3-padding-large w3-hide"
        tabindex="-1"
        type="file"
        :accept="reference.evidence?.allowedExtensions.join(',')"
        multiple
        aria-label="Choose Evidence Files"
        :disabled="uploading || preparing"
        @change="choose"
      />
      <input
        ref="camera"
        class="sr-only w3-padding-large w3-hide"
        tabindex="-1"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        capture="environment"
        aria-label="Take Evidence Photo"
        :disabled="uploading || preparing"
        @change="choose"
      />
      <div class="evidence-actions w3-section">
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="
            uploading ||
            preparing ||
            files.length >= (reference.evidence?.maximumFiles ?? 0) ||
            !category
          "
          @click="picker?.click()"
        >
          <Paperclip
            class="w3-margin-right"
            :size="16"
          />
          Attach Files
        </button>
        <button
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          :disabled="
            uploading ||
            preparing ||
            files.length >= (reference.evidence?.maximumFiles ?? 0) ||
            !category
          "
          @click="camera?.click()"
        >
          <Camera
            class="w3-margin-right"
            :size="16"
          />
          Take Photo
        </button>
      </div>
      <p class="evidence-hint w3-small w3-text-grey w3-block">
        PDF, JPEG, PNG or WebP · optimised images up to 5 MB. Original photos retained; 25 MB total
        per case.
      </p>
      <ul
        v-if="pending.length"
        class="evidence-list pending-evidence w3-ul w3-section"
      >
        <li
          v-for="(item, index) in pending"
          :key="index"
        >
          <div>
            <img
              v-if="item.preview"
              :src="item.preview"
              alt="Selected evidence preview"
              class="pending-image-preview w3-image w3-round-large"
            />
            <strong class="w3-block w3-small">{{ item.file.name }}</strong>
            <small
              v-if="item.original"
              class="w3-small"
            >
              Optimised copy · original retained ({{ size(item.original.size) }})
            </small>
            <small class="w3-small">
              {{ evidenceCategoryLabel(item.category) }} · {{ size(item.file.size) }}
            </small>
            <p
              v-if="item.error"
              role="alert"
              class="evidence-error w3-text-red w3-small"
            >
              {{ item.error }}
            </p>
          </div>
          <button
            class="icon-button w3-button w3-round-large"
            :disabled="uploading || preparing"
            :aria-label="'Remove ' + item.file.name"
            @click="removePending(item)"
          >
            <X :size="16" />
          </button>
        </li>
      </ul>
      <p
        v-if="preparing"
        role="status"
        class="field-hint w3-small w3-text-grey w3-block"
      >
        Preparing photo…
      </p>
      <div
        v-if="uploading"
        role="status"
        class="upload-progress"
      >
        <progress
          :value="completed"
          :max="uploadTotal"
        />
        <small class="w3-small">
          Sending {{ activeUpload }} · {{ completed }} of {{ uploadTotal }} files saved
        </small>
      </div>
      <button
        v-if="pending.length"
        class="button button-primary evidence-upload w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="uploading || preparing"
        @click="upload"
      >
        {{
          uploading
            ? 'Uploading…'
            : 'Upload ' + pending.length + (pending.length === 1 ? ' File' : ' Files')
        }}
      </button>
    </template>
    <p
      v-if="loading"
      class="evidence-hint w3-small w3-text-grey w3-block"
      role="status"
    >
      Loading evidence…
    </p>
    <ul
      v-else-if="files.length"
      class="evidence-list w3-ul w3-section"
    >
      <li
        v-for="file in files"
        :key="file.id"
        class="evidence-file-row w3-padding-16"
      >
        <FileText :size="19" />
        <div class="evidence-file-content w3-section">
          <strong class="w3-block">{{ evidenceCategoryLabel(file.category) }}</strong>
          <small
            v-if="file.deletedAt"
            class="evidence-removed w3-small"
          >
            Removed By {{ file.deletedBy || 'Recorded Account Holder' }} ·
            {{ formatDate(file.deletedAt) }}
          </small>
          <span
            v-if="file.canView || file.deletedAt"
            class="evidence-filename w3-block w3-small w3-text-grey"
          >
            {{ file.fileName }}
          </span>
          <small
            v-if="file.canView"
            class="w3-small"
          >
            {{ size(file.sizeBytes) }} · {{ file.uploadedBy }}
          </small>
          <small
            v-else-if="!file.deletedAt"
            class="w3-small"
          >
            <LockKeyhole
              class="w3-margin-right"
              :size="13"
            />
            Access required
          </small>
          <time
            class="w3-block w3-small w3-text-grey"
            :datetime="file.createdAt"
          >
            {{ formatDate(file.createdAt) }}
          </time>
        </div>
        <div class="evidence-file-actions w3-row-padding w3-section">
          <button
            v-if="file.canView"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="preview = file"
          >
            <Eye
              class="w3-margin-right"
              :size="16"
            />
            View
          </button>
          <button
            v-if="props.canManageAccess && file.canManageAccess"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="access = file"
          >
            <LockKeyhole
              class="w3-margin-right"
              :size="16"
            />
            Access
          </button>
          <button
            v-if="
              !file.deletedAt && (file.canRemoveOwn || (props.canManageAccess && file.canRemove))
            "
            class="button button-secondary evidence-remove w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            @click="removal = file"
          >
            <Trash2
              class="w3-margin-right"
              :size="16"
            />
            Remove File
          </button>
        </div>
      </li>
    </ul>
    <p
      v-else-if="!loading && !error"
      class="evidence-hint w3-small w3-text-grey w3-block"
    >
      No evidence attached yet.
    </p>
    <EvidenceRemovalDialog
      v-if="removal"
      :key="caseId + removal.id"
      :case-id="caseId"
      :file="removal"
      @close="removal = null"
      @removed="evidenceRemoved"
    />
    <EvidencePreview
      v-if="preview"
      :key="caseId + preview.id"
      :case-id="caseId"
      :file="preview"
      @close="preview = null"
    />
    <EvidenceAccessPanel
      v-if="props.canManageAccess && access"
      :key="caseId + access.id"
      :case-id="caseId"
      :file="access"
      @close="access = null"
      @changed="load"
    />
  </section>
</template>
