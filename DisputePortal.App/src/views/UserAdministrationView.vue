<script setup lang="ts">
import { clientOption } from '../utils/portalContent'
import { useFeedback } from '../composables/useFeedback'

import PasswordInput from '../components/PasswordInput.vue'
import ChoiceSheet from '../components/ChoiceSheet.vue'
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ChevronRight, Plus, Search, ShieldCheck, Users, X } from '@lucide/vue'
import { api, ApiError } from '../api'
import { useQuietLiveRefresh } from '../composables/useQuietLiveRefresh'
import { useAuthStore } from '../stores/auth'
import type { StaffUser } from '../operations'
import { reference } from '../portalReference'
import { isAssignableRole, userRoles } from '../utils/userAccess'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'

const auth = useAuthStore()
const users = ref<StaffUser[]>([]),
  loading = ref(true),
  saving = ref(false)
const error = ref(''),
  success = ref(''),
  editorError = ref('')
const search = ref(''),
  roleFilter = ref(''),
  statusFilter = ref(''),
  page = ref(1)
const dialog = ref<HTMLDialogElement | null>(null),
  editor = ref<'create' | 'edit' | null>(null)
const selected = ref<StaffUser | null>(null)
const draft = reactive({
  displayName: '',
  firstName: '',
  middleName: '',
  lastName: '',
  email: '',
  role: '',
  password: '',
  status: 'Active',
  capacity: 0
})
let request: AbortController | undefined,
  disposed = false
const self = computed(() => selected.value?.id === auth.user?.id)
const nameChanged = computed(
  () =>
    draft.firstName !== (selected.value?.firstName ?? '') ||
    draft.middleName !== (selected.value?.middleName ?? '') ||
    draft.lastName !== (selected.value?.lastName ?? '')
)
const accessLocked = computed(() => self.value || (selected.value?.activeCases ?? 0) > 0)
const filtered = computed(() => {
  const term = search.value.trim().toLocaleLowerCase()
  return users.value.filter(
    (user) =>
      (!roleFilter.value || user.role === roleFilter.value) &&
      (!statusFilter.value || user.status === statusFilter.value) &&
      (!term ||
        [user.displayName, user.email, userRoles[user.role]?.label].some((value) =>
          value?.toLocaleLowerCase().includes(term)
        ))
  )
})
const pageSize = computed(() => clientOption('staffPageSize'))
const pages = computed(() => Math.max(1, Math.ceil(filtered.value.length / pageSize.value)))
const visible = computed(() =>
  filtered.value.slice((page.value - 1) * pageSize.value, page.value * pageSize.value)
)
watch([search, roleFilter, statusFilter], () => {
  page.value = 1
})
const initials = (name: string) =>
  name
    .split(' ')
    .filter(Boolean)
    .map((part) => part[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
async function fetchData(quiet = false) {
  request?.abort()
  request = new AbortController()
  if (!quiet) {
    loading.value = true
    error.value = ''
  }
  const controller = request
  try {
    const result = await api.get<StaffUser[]>('/api/v1/operations/users', controller.signal)
    if (!disposed && !controller.signal.aborted) {
      users.value = result
      error.value = ''
      page.value = Math.min(page.value, pages.value)
    }
  } catch (cause) {
    if (!controller.signal.aborted && !disposed && (!quiet || !users.value.length))
      error.value = cause instanceof ApiError ? cause.message : 'Users are temporarily unavailable.'
  } finally {
    if (!disposed && !controller.signal.aborted) {
      if (!quiet) loading.value = false
      liveReady()
    }
  }
}
async function load() {
  await fetchData()
}
const liveReady = useQuietLiveRefresh(
  () => fetchData(true),
  () => loading.value,
  ['administration']
)
async function openEditor(user?: StaffUser) {
  selected.value = user ? { ...user } : null
  Object.assign(draft, {
    displayName: user?.displayName ?? '',
    firstName: user?.firstName ?? '',
    middleName: user?.middleName ?? '',
    lastName: user?.lastName ?? '',
    email: user?.email ?? '',
    role: user?.role ?? '',
    password: '',
    status: user?.status ?? 'Active',
    capacity: user?.capacity ?? 0
  })
  editorError.value = ''
  editor.value = user ? 'edit' : 'create'
  await nextTick()
  dialog.value?.showModal()
}
function closeEditor() {
  if (saving.value) return
  dialog.value?.close()
  draft.password = ''
  editor.value = null
  selected.value = null
}
function cancelEditor(event: Event) {
  event.preventDefault()
  closeEditor()
}
async function save() {
  if (saving.value || self.value) return
  saving.value = true
  editorError.value = ''
  success.value = ''
  try {
    if (editor.value === 'create') {
      await api.post('/api/v1/operations/users', {
        email: draft.email.trim(),
        displayName: draft.firstName.trim() + ' ' + draft.lastName.trim(),
        firstName: draft.firstName.trim(),
        middleName: draft.middleName.trim() || null,
        lastName: draft.lastName.trim(),
        role: draft.role,
        password: draft.password
      })
      success.value = 'User created. Share the initial password through an approved secure channel.'
    } else if (selected.value) {
      await api.post('/api/v1/operations/users/' + selected.value.id, {
        role: draft.role,
        status: draft.status,
        capacity: draft.capacity,
        ...(nameChanged.value
          ? {
              firstName: draft.firstName.trim(),
              middleName: draft.middleName.trim() || null,
              lastName: draft.lastName.trim()
            }
          : {})
      })
      success.value = 'Access updated. Previous sessions have been signed out.'
    }
    saving.value = false
    closeEditor()
    await load()
  } catch (cause) {
    editorError.value =
      cause instanceof ApiError ? cause.message : 'Changes were not saved. Please try again.'
  } finally {
    saving.value = false
  }
}
onMounted(load)
onUnmounted(() => {
  disposed = true
  request?.abort()
  draft.password = ''
})
useFeedback(error, success, undefined)
</script>

<template>
  <AppShell>
    <div class="user-admin-page">
      <PageHeading
        eyebrow="SUPER ADMIN"
        title="Users &amp; Access"
      />
      <div class="user-admin-toolbar w3-section">
        <span>{{ users.length }} users</span>
        <button
          class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
          @click="openEditor()"
        >
          <Plus
            class="w3-margin-right"
            :size="17"
          />
          Add User
        </button>
      </div>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
        <button
          class="text-button w3-button w3-text-black w3-round-large"
          @click="load"
        >
          Retry
        </button>
      </p>
      <p
        v-if="success"
        class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
        role="status"
      >
        {{ success }}
      </p>
      <section
        class="surface-panel user-directory w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        aria-label="User Directory"
      >
        <div class="user-directory-filters w3-section">
          <label class="search-field w3-block w3-text-dark-grey w3-medium">
            <Search :size="17" />
            <input
              v-model="search"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="search"
              maxlength="100"
              placeholder="Search name or email"
              aria-label="Search Users"
            />
          </label>
          <div class="user-filter-row w3-section">
            <select
              v-model="roleFilter"
              class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
              aria-label="Filter Users By Role"
            >
              <option value="">All roles</option>
              <option
                v-for="(item, key) in userRoles"
                :key="key"
                :value="key"
              >
                {{ item.label }}
              </option>
            </select>
            <ChoiceSheet
              v-model="statusFilter"
              label="Filter Users By Status"
              :options="[
                { value: '', label: 'All Statuses' },
                ...(reference.staffStatuses ?? []).map((value) => ({ value, label: value }))
              ]"
            />
          </div>
        </div>
        <p
          v-if="loading"
          class="user-directory-state w3-small w3-text-grey"
          role="status"
        >
          Loading users…
        </p>
        <template v-else>
          <button
            v-for="user in visible"
            :key="user.id"
            class="user-directory-row w3-button w3-block w3-left-align w3-border-bottom w3-padding-16 w3-round-large"
            :aria-label="'Manage ' + user.displayName"
            @click="openEditor(user)"
          >
            <span class="user-directory-avatar w3-tag w3-pale-blue w3-round w3-margin-bottom">
              {{ initials(user.displayName) }}
            </span>
            <span class="user-directory-copy w3-block">
              <strong>
                {{ user.displayName }}
                <small
                  v-if="user.id === auth.user?.id"
                  class="w3-small"
                >
                  You
                </small>
              </strong>
              <span class="user-directory-email w3-block w3-small w3-text-grey">
                {{ user.email }}
              </span>
              <span class="user-row-role w3-block w3-small">
                {{ userRoles[user.role]?.label ?? user.role }}
                <i aria-hidden="true">·</i>
                <span :class="{ 'user-status-inactive': user.status !== 'Active' }">
                  {{ user.status }}
                </span>
              </span>
            </span>
            <ChevronRight :size="17" />
          </button>
          <div
            v-if="!visible.length"
            class="user-directory-empty w3-center w3-padding-32"
          >
            <Users :size="25" />
            <strong>No matching users</strong>
            <button
              class="text-button w3-button w3-text-black w3-round-large"
              @click="
                () => {
                  search = ''
                  roleFilter = ''
                  statusFilter = ''
                }
              "
            >
              Clear Filters
            </button>
          </div>
          <div
            v-if="pages > 1"
            class="user-directory-pagination w3-bar w3-section w3-center"
          >
            <span>{{ page }} / {{ pages }} · {{ filtered.length }} users</span>
            <div>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="page === 1"
                @click="page--"
              >
                Previous
              </button>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="page >= pages"
                @click="page++"
              >
                Next
              </button>
            </div>
          </div>
        </template>
      </section>
      <p class="user-admin-footnote w3-small w3-text-grey">
        <ShieldCheck
          class="w3-margin-right"
          :size="14"
        />
        <span>Access changes are audited.</span>
      </p>
    </div>
    <dialog
      ref="dialog"
      class="user-editor w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m10 l8"
      aria-labelledby="user-editor-title"
      @cancel="cancelEditor"
    >
      <div class="w3-bar">
        <template v-if="editor">
          <header class="user-editor-heading w3-bar w3-padding-16">
            <button
              class="icon-button w3-button w3-round-large w3-right"
              type="button"
              aria-label="Close User Editor"
              :disabled="saving"
              @click="closeEditor"
            >
              <X :size="20" />
            </button>
            <div>
              <p>{{ editor === 'create' ? 'NEW ACCOUNT' : 'ACCOUNT ACCESS' }}</p>
              <h2
                id="user-editor-title"
                class="w3-sans-serif w3-large"
              >
                {{ editor === 'create' ? 'Add User' : selected?.displayName }}
              </h2>
            </div>
          </header>
          <form
            class="user-editor-form"
            @submit.prevent="save"
          >
            <div class="user-editor-fields w3-section">
              <p
                v-if="editorError"
                role="alert"
                class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
              >
                {{ editorError }}
              </p>
              <template v-if="editor === 'create'">
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Email Address
                  <input
                    v-model.trim="draft.email"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    type="email"
                    required
                    maxlength="320"
                    autocomplete="off"
                  />
                </label>
              </template>
              <p
                v-else
                class="user-editor-email w3-small w3-text-grey"
              >
                {{ selected?.email }}
              </p>
              <div class="user-editor-name-fields w3-row-padding">
                <div class="w3-col s12 m4 w3-margin-bottom">
                  <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                    First Name
                    <input
                      v-model.trim="draft.firstName"
                      class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                      :required="editor === 'create' || nameChanged"
                      maxlength="100"
                      autocomplete="given-name"
                      :disabled="self"
                    />
                  </label>
                </div>
                <div class="w3-col s12 m4 w3-margin-bottom">
                  <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                    <span>
                      Middle Name
                      <small class="w3-small">(Optional)</small>
                    </span>
                    <input
                      v-model.trim="draft.middleName"
                      class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                      maxlength="100"
                      autocomplete="additional-name"
                      :disabled="self"
                    />
                  </label>
                </div>
                <div class="w3-col s12 m4 w3-margin-bottom">
                  <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                    Surname
                    <input
                      v-model.trim="draft.lastName"
                      class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                      :required="editor === 'create' || nameChanged"
                      maxlength="100"
                      autocomplete="family-name"
                      :disabled="self"
                    />
                  </label>
                </div>
              </div>
              <p
                v-if="editor === 'edit' && (!selected?.firstName || !selected?.lastName)"
                class="user-editor-note w3-small w3-text-grey"
              >
                Names can be completed separately from access changes.
              </p>
              <div class="access-field w3-section">
                <span>Role</span>
                <ChoiceSheet
                  v-model="draft.role"
                  label="Role"
                  :disabled="accessLocked"
                  :options="
                    Object.entries(userRoles).map(([value, role]) => ({ value, label: role.label }))
                  "
                />
              </div>
              <p class="user-role-description w3-small w3-text-grey">
                {{ userRoles[draft.role]?.description }}
              </p>
              <template v-if="editor === 'create'">
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Initial Password
                  <PasswordInput
                    v-model="draft.password"
                    required
                    :minlength="reference.password?.minimum"
                    :maxlength="reference.password?.maximum ?? undefined"
                    autocomplete="new-password"
                  />
                </label>
                <small class="w3-small">
                  12+ characters with uppercase, lowercase, a number and a symbol.
                </small>
              </template>
              <template v-else>
                <div class="user-editor-grid w3-row-padding">
                  <div class="w3-col s12 m6 w3-margin-bottom">
                    <div class="access-field w3-section">
                      <span>Account Status</span>
                      <ChoiceSheet
                        v-model="draft.status"
                        label="Account Status"
                        :disabled="accessLocked"
                        :options="
                          (reference.staffStatuses ?? []).map((value) => ({ value, label: value }))
                        "
                      />
                    </div>
                  </div>
                  <div
                    v-if="isAssignableRole(draft.role)"
                    class="w3-col s12 m6 w3-margin-bottom"
                  >
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Case Capacity
                      <input
                        v-model.number="draft.capacity"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        type="number"
                        required
                        :min="reference.staffCapacity?.minimum"
                        :max="reference.staffCapacity?.maximum ?? undefined"
                        :disabled="self"
                      />
                    </label>
                  </div>
                </div>
                <p
                  v-if="isAssignableRole(draft.role)"
                  class="user-workload w3-small w3-text-grey"
                >
                  {{ selected?.activeCases }}
                  open cases ·
                  {{ selected?.availability }}
                </p>
                <p
                  v-if="self"
                  class="user-editor-note w3-small w3-text-grey"
                >
                  Update your own name in Profile. Another super admin must change your access.
                </p>
                <p
                  v-else-if="accessLocked"
                  class="user-editor-note w3-small w3-text-grey"
                >
                  Reassign this user’s open cases before changing their role or status.
                </p>
              </template>
            </div>
            <footer class="user-editor-footer w3-bar w3-padding-16">
              <button
                v-if="!self"
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="saving"
              >
                {{ saving ? 'Saving…' : editor === 'create' ? 'Create User' : 'Save Access' }}
              </button>
              <button
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                type="button"
                :disabled="saving"
                @click="closeEditor"
              >
                {{ self ? 'Done' : 'Cancel' }}
              </button>
            </footer>
          </form>
        </template>
      </div>
    </dialog>
  </AppShell>
</template>
