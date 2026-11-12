<script setup lang="ts">
import { customerActor } from '../utils/customerPresentation'

import DisclosurePanel from './DisclosurePanel.vue'
import { useConversationActions } from '../composables/useConversationActions'
import { useFeedback } from '../composables/useFeedback'
import { requestId } from '../utils/requestId'
import { useOnline } from '../composables/useOnline'
import { useAutosizeTextarea } from '../composables/useAutosizeTextarea'
import { useLiveRefresh } from '../composables/useLiveRefresh'
import { isClosedStatus } from '../utils/casePresentation'
import * as portalMetadata from '../portalReference'
import { computed, nextTick, onUnmounted, ref, watch, useTemplateRef } from 'vue'
import { MessageCircle, ArrowLeft, Maximize2, Ellipsis } from '@lucide/vue'
import { formatDate, formatMoney } from '../utils/format'
import { api, ApiError } from '../api'
import { useAuthStore } from '../stores/auth'
import CaseClaimAction from './CaseClaimAction.vue'
import type { SupportThread, SupportMessage, CaseOperations, StaffUser } from '../operations'
const props = defineProps<{
  fullscreen?: boolean
  backTo?: string
  relatedDisputeId?: string
  relatedCaseContext?: string
  subject?: string
  transactionId?: string
  amount?: number | null
  currency?: string | null
  accountNumber?: string
  transactionAt?: string | null
  disputeId?: string
  threadId?: string
  readonly?: boolean
  title?: string
  reference?: string
  caseDate?: string | null
  status?: string
  lastReadSequence?: number
}>()
const emit = defineEmits<{
    sent: [id: string]
    read: []
    changed: [accessLost: boolean]
    new: []
  }>(),
  auth = useAuthStore()
const caseThreads = ref<SupportThread[]>([])
const { online } = useOnline()
const olderLoading = ref(false),
  olderAvailable = ref(false),
  historyMode = ref(false),
  newMessages = ref(0)
let latestSequence = 0,
  pendingSend: { key: string; body: string; subject: string } | undefined,
  olderRequest: AbortController | undefined
const messages = ref<SupportMessage[]>([]),
  thread = ref(''),
  body = ref(''),
  subject = ref(
    props.subject ||
      (props.disputeId || props.transactionId
        ? props.title || 'Transaction enquiry'
        : props.relatedDisputeId
          ? 'Enquiry about ' + (props.reference || 'a closed dispute')
          : props.threadId
            ? props.title || 'Support enquiry'
            : '')
  )
const metadata = ref<SupportThread | null>(null)
const cleanRelatedContext = computed(() =>
  (props.relatedCaseContext || metadata.value?.relatedCaseContext || '')
    .split(' · ')
    .filter((part) => portalMetadata.statusReferences[part]?.closed !== true)
    .join(' · ')
)
const newConversationCase = computed(
  () => props.disputeId || props.relatedDisputeId || metadata.value?.relatedDisputeId
)
const error = ref(''),
  sending = ref(false),
  loading = ref(false),
  permitted = ref(false)
const actionDialog = useTemplateRef<HTMLDialogElement>('actionDialog')
const {
  assignmentTarget,
  assignmentChoices,
  menuOpen,
  action,
  actionReason,
  actionBusy,
  cancelAction,
  startAction,
  completeAction
} = useConversationActions(metadata, permitted, error, actionDialog, (accessLost) =>
  emit('changed', accessLost)
)
const canCloseConversation = computed(
  () => metadata.value?.canClose === true && (!props.disputeId || caseClosed.value)
)
const conversationClosed = computed(() => metadata.value?.status === 'Closed')
const composer = ref<HTMLTextAreaElement | null>(null)
useAutosizeTextarea(composer, body)
const scroller = ref<HTMLElement | null>(null),
  unreadFrom = ref<number | null>(null)
const ownership = ref<CaseOperations | null>(null),
  currentStaff = ref<StaffUser>()
const linkedStatus = ref(props.status)
const currentStatus = computed(() =>
  props.status && isClosedStatus(props.status)
    ? props.status
    : metadata.value?.caseStatus || ownership.value?.status || linkedStatus.value || props.status
)
const caseClosed = computed(
  () => !props.relatedDisputeId && !!currentStatus.value && isClosedStatus(currentStatus.value)
)
const readOnly = computed(
  () =>
    props.readonly ||
    conversationClosed.value ||
    caseClosed.value ||
    (!!props.disputeId && !currentStatus.value) ||
    !permitted.value
)
let generation = 0,
  disposed = false,
  initial = true,
  lastRead = 0,
  reading = false
let request: AbortController | undefined, scrollTimer: ReturnType<typeof setTimeout> | undefined
const date = (value: string) =>
  formatDate(value, { day: 'numeric', month: 'short', year: 'numeric' })
const time = (value: string) => formatDate(value, { hour: '2-digit', minute: '2-digit' })
const presentedMessages = computed(() =>
  messages.value.map((message, index) => ({
    ...message,
    bodyLines: message.body.split(/\r?\n/),
    authorRole: message.authorRole
      ? portalMetadata.userRoles[message.authorRole]?.label || message.authorRole
      : undefined,
    dayLabel: date(message.createdAt),
    timeLabel: time(message.createdAt),
    fullDateLabel: formatDate(message.createdAt),
    startsDay: index === 0 || date(messages.value[index - 1]!.createdAt) !== date(message.createdAt)
  }))
)
async function markVisible(force = false) {
  if (reading || !thread.value || !scroller.value || document.visibilityState !== 'visible') return
  const bounds = scroller.value.getBoundingClientRect()
  const elements = scroller.value.querySelectorAll<HTMLElement>('[data-message-sequence]')
  let seq = lastRead
  for (let index = elements.length - 1; index >= 0; index--) {
    const element = elements[index]!
    const bottom = element.getBoundingClientRect().bottom
    if (
      bottom <= Math.min(bounds.bottom, window.innerHeight) + 2 &&
      bottom >= Math.max(bounds.top, 0)
    ) {
      seq = Math.max(lastRead, Number(element.dataset.messageSequence))
      break
    }
  }
  const message = messages.value.find((m) => m.sequence === seq)
  if (!message || (seq <= lastRead && !force)) return
  reading = true
  try {
    await api.post('/api/v1/support/' + thread.value + '/read', { messageId: message.id })
    if (!disposed) {
      lastRead = seq
      emit('read')
    }
  } catch {
    /* Retry a read receipt when the messages become visible again. */
  } finally {
    reading = false
  }
}
function onScroll() {
  if (scrollTimer) clearTimeout(scrollTimer)
  scrollTimer = setTimeout(() => void markVisible(), 250)
}
async function load(manual = false) {
  if (loading.value || sending.value || olderLoading.value || !online.value) return
  const current = ++generation
  request?.abort()
  const controller = new AbortController()
  request = controller
  loading.value = true
  try {
    let id = props.threadId,
      read = props.lastReadSequence ?? 0
    if (props.disputeId && (initial || !id)) {
      const available = await api.get<SupportThread[]>(
        '/api/v1/support?disputeId=' + props.disputeId,
        controller.signal
      )
      if (current === generation) caseThreads.value = available
      const found = available
        .filter((t) => t.disputeId === props.disputeId)
        .sort((a, b) => Number(a.status === 'Closed') - Number(b.status === 'Closed'))[0]
      if (!id) {
        id = found?.id
        read = found?.lastReadSequence ?? 0
      }
      if (current === generation) linkedStatus.value = found?.caseStatus ?? props.status
    }
    const after =
      !initial && !manual ? latestSequence || messages.value.at(-1)?.sequence : undefined
    const suffix = after === undefined ? '' : '?afterSequence=' + after
    const [messagePage, caseInfo, roster, info] = await Promise.all([
      id
        ? api.cursorPage<SupportMessage>(
            '/api/v1/support/' + id + '/messages' + suffix,
            controller.signal
          )
        : Promise.resolve({ items: [], hasMore: false }),
      props.disputeId
        ? api.get<CaseOperations>('/api/v1/operations/cases/' + props.disputeId, controller.signal)
        : Promise.resolve(null),
      auth.isStaffWorkspace && props.disputeId && !currentStaff.value
        ? api.get<StaffUser[]>('/api/v1/operations/staff', controller.signal)
        : Promise.resolve([]),
      id
        ? api.get<SupportThread>('/api/v1/support/' + id, controller.signal)
        : Promise.resolve(null)
    ])
    if (disposed || current !== generation) return
    const result = messagePage.items
    const bottom =
      !scroller.value ||
      scroller.value.scrollHeight - scroller.value.scrollTop - scroller.value.clientHeight < 70
    thread.value = id ?? ''
    if (result.length) latestSequence = result.at(-1)!.sequence
    if (after === undefined) {
      messages.value = result
      olderAvailable.value = messagePage.hasMore
      historyMode.value = false
      newMessages.value = 0
    } else if (historyMode.value) {
      newMessages.value += result.length
    } else if (result.length) {
      const known = new Set(messages.value.map((item) => item.id))
      const merged = [...messages.value, ...result.filter((item) => !known.has(item.id))]
      if (merged.length > 400) olderAvailable.value = true
      messages.value = merged.slice(-400)
    }
    ownership.value = caseInfo
    if (roster.length) currentStaff.value = roster.find((person) => person.id === auth.user?.id)
    metadata.value = info
    if (info?.caseStatus) linkedStatus.value = info.caseStatus
    permitted.value =
      info?.canReply === true || (!id && caseInfo?.actions?.canStartConversation === true)
    if (initial) {
      lastRead = read
      unreadFrom.value =
        result.find((m) => m.sequence > read && m.authorId !== auth.user?.id)?.sequence ?? null
    }
    await nextTick()
    if (scroller.value) {
      if (initial || bottom) scroller.value.scrollTop = scroller.value.scrollHeight
    }
    const opening = initial
    initial = false
    error.value = ''
    await markVisible(opening)
  } catch (cause) {
    if (!disposed && current === generation && !controller.signal.aborted) {
      permitted.value = false
      error.value =
        cause instanceof ApiError ? cause.message : 'Messages are temporarily unavailable.'
    }
  } finally {
    if (!disposed && current === generation) loading.value = false
  }
}
async function loadOlder() {
  if (olderLoading.value || !thread.value || !messages.value.length) return
  olderLoading.value = true
  const current = generation,
    context = thread.value
  olderRequest?.abort()
  const olderController = new AbortController()
  olderRequest = olderController
  const first = messages.value[0]!,
    element = scroller.value?.querySelector<HTMLElement>(
      '[data-message-sequence="' + first.sequence + '"]'
    ),
    position = element?.getBoundingClientRect().top ?? 0
  try {
    const olderPage = await api.cursorPage<SupportMessage>(
      '/api/v1/support/' + context + '/messages?beforeSequence=' + first.sequence,
      olderController.signal
    )
    if (disposed || current !== generation || thread.value !== context) return
    const older = olderPage.items
    olderAvailable.value = olderPage.hasMore
    historyMode.value = true
    const known = new Set(messages.value.map((x) => x.id))
    messages.value = [...older.filter((x) => !known.has(x.id)), ...messages.value].slice(0, 600)
    await nextTick()
    const anchor = scroller.value?.querySelector<HTMLElement>(
      '[data-message-sequence="' + first.sequence + '"]'
    )
    if (anchor && scroller.value)
      scroller.value.scrollTop += anchor.getBoundingClientRect().top - position
  } catch {
    if (!disposed && current === generation && !olderController.signal.aborted)
      error.value = 'Older messages could not be loaded. Retry when connected.'
  } finally {
    if (current === generation) olderLoading.value = false
  }
}
async function send() {
  if (
    !online.value ||
    sending.value ||
    readOnly.value ||
    body.value.trim().length < (portalMetadata.reference.supportBody?.minimum ?? Infinity)
  )
    return
  if (
    !pendingSend ||
    pendingSend.body !== body.value.trim() ||
    pendingSend.subject !== subject.value.trim()
  )
    pendingSend = { key: requestId(), body: body.value.trim(), subject: subject.value.trim() }
  const current = generation
  sending.value = true
  error.value = ''
  let committed = false
  try {
    const result = await api.post<{ id: string }>('/api/v1/support', {
      threadId: thread.value || null,
      ...(thread.value
        ? {}
        : props.relatedDisputeId
          ? { relatedDisputeId: props.relatedDisputeId }
          : { disputeId: props.disputeId || null, transactionId: props.transactionId || null }),
      subject: subject.value.trim(),
      body: body.value.trim(),
      clientMessageId: pendingSend.key
    })
    if (disposed || current !== generation) return
    committed = true
    pendingSend = undefined
    thread.value = result.id
    body.value = ''
    const after = historyMode.value ? undefined : messages.value.at(-1)?.sequence
    const updatedPage = await api.cursorPage<SupportMessage>(
      '/api/v1/support/' +
        result.id +
        '/messages' +
        (after === undefined ? '' : '?afterSequence=' + after)
    )
    if (disposed || current !== generation) return
    const updated = updatedPage.items
    if (historyMode.value) olderAvailable.value = updatedPage.hasMore
    const known = new Set(messages.value.map((item) => item.id))
    const merged = historyMode.value
      ? updated
      : [...messages.value, ...updated.filter((item) => !known.has(item.id))]
    if (merged.length > 400) olderAvailable.value = true
    messages.value = merged.slice(-400)
    historyMode.value = false
    newMessages.value = 0
    if (updated.length) latestSequence = updated.at(-1)!.sequence
    await nextTick()
    if (scroller.value) scroller.value.scrollTop = scroller.value.scrollHeight
    await markVisible()
    emit('sent', result.id)
  } catch (cause) {
    if (!disposed && current === generation)
      error.value = committed
        ? 'Message saved. Reconnecting to the conversation…'
        : cause instanceof ApiError
          ? cause.message
          : 'Message not sent. Your draft is still here.'
  } finally {
    if (!disposed && current === generation) sending.value = false
  }
}
useLiveRefresh(() => load(), ['support', 'disputes'])
watch(
  () => props.status,
  (value) => {
    linkedStatus.value = value
  }
)
watch(
  () => [props.disputeId, props.threadId, props.transactionId, props.relatedDisputeId],
  () => {
    request?.abort()
    olderRequest?.abort()
    generation++
    loading.value = false
    olderLoading.value = false
    sending.value = false
    body.value = ''
    subject.value =
      props.subject ||
      (props.disputeId || props.transactionId
        ? props.title || 'Transaction enquiry'
        : props.relatedDisputeId
          ? 'Enquiry about ' + (props.reference || 'a closed dispute')
          : props.threadId
            ? props.title || 'Support enquiry'
            : '')
    messages.value = []
    latestSequence = 0
    historyMode.value = false
    olderAvailable.value = false
    newMessages.value = 0
    pendingSend = undefined
    ownership.value = null
    metadata.value = null
    action.value = null
    linkedStatus.value = props.status
    initial = true
    permitted.value = false
    void load()
  }
)
onUnmounted(() => {
  disposed = true
  generation++
  request?.abort()
  olderRequest?.abort()
  if (scrollTimer) clearTimeout(scrollTimer)
})
useFeedback(error, undefined, undefined)
</script>
<template>
  <section
    class="chat-panel w3-theme-l5 w3-text-black w3-responsive"
    :class="[
      fullscreen ? 'w3-overlay w3-show' : ' w3-round-large',
      { 'chat-empty': !messages.length }
    ]"
  >
    <header
      class="chat-heading w3-content w3-container w3-white w3-border-bottom w3-padding-16 w3-bar"
      aria-label="Conversation Details"
    >
      <button
        v-if="canCloseConversation || metadata?.canEscalate || metadata?.canAssign"
        class="icon-button chat-menu-toggle w3-button w3-round-large w3-right"
        type="button"
        aria-label="Conversation Actions"
        :aria-expanded="menuOpen"
        @click="menuOpen = !menuOpen"
      >
        <Ellipsis :size="21" />
      </button>
      <RouterLink
        v-if="fullscreen"
        :to="backTo || '/support'"
        class="icon-button chat-back w3-button w3-round-large w3-left w3-margin-right"
        aria-label="Back To Messages"
      >
        <ArrowLeft :size="21" />
      </RouterLink>
      <RouterLink
        v-if="!fullscreen && (thread || disputeId)"
        :to="{ path: '/support', query: thread ? { thread } : { dispute: disputeId } }"
        class="icon-button w3-button w3-round-large w3-right"
        aria-label="Open Full Screen"
      >
        <Maximize2 :size="19" />
      </RouterLink>
      <div class="w3-rest">
        <span class="chat-workspace w3-block w3-small w3-text-grey">
          {{ auth.isStaffWorkspace ? 'Staff Workspace' : 'Customer Workspace' }}
        </span>
        <h2 class="w3-large w3-sans-serif">
          <b>
            {{ title || (auth.isStaffWorkspace ? 'Case Messages' : 'Chat with us') }}
          </b>
        </h2>
      </div>
    </header>
    <nav
      v-if="
        transactionId ||
        metadata?.transactionId ||
        disputeId ||
        relatedDisputeId ||
        metadata?.disputeId ||
        metadata?.relatedDisputeId
      "
      class="chat-context-actions w3-container w3-padding-16 w3-border-bottom w3-row-padding w3-content"
      aria-label="Linked Records"
    >
      <div class="w3-col s12 m6">
        <RouterLink
          v-if="transactionId || metadata?.transactionId"
          :to="
            (auth.isStaffWorkspace ? '/admin/transactions/' : '/transactions/') +
            (transactionId || metadata?.transactionId)
          "
          class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          aria-label="Open Linked Transaction"
        >
          <span>Open Transaction</span>
          <small
            v-if="amount != null || metadata?.amount != null"
            class="w3-small w3-block w3-text-grey"
          >
            {{
              formatMoney(
                amount ?? metadata?.amount ?? 0,
                currency || metadata?.currency || undefined
              )
            }}
          </small>
        </RouterLink>
      </div>
      <div class="w3-col s12 m6">
        <RouterLink
          v-if="disputeId || relatedDisputeId || metadata?.disputeId || metadata?.relatedDisputeId"
          :to="
            (auth.isStaffWorkspace ? '/admin/disputes/' : '/disputes/') +
            (disputeId || relatedDisputeId || metadata?.disputeId || metadata?.relatedDisputeId)
          "
          class="button button-secondary w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          aria-label="Open Linked Case"
        >
          <span>Open Case</span>
          <small
            v-if="reference"
            class="w3-small w3-block w3-text-grey"
          >
            {{ reference }}
          </small>
        </RouterLink>
      </div>
    </nav>
    <div
      v-if="metadata"
      class="conversation-state w3-bar w3-container w3-small w3-text-grey w3-padding-16 w3-border-bottom"
    >
      <span class="w3-bar-item">{{ metadata.concernLabel || 'Support' }}</span>
      <span class="w3-bar-item w3-right">
        {{ metadata.status === 'Closed' ? 'Closed' : 'Open' }}
      </span>
    </div>
    <nav
      v-if="caseThreads.length > 1"
      class="conversation-history w3-container w3-responsive w3-padding-16"
      aria-label="Case Conversation History"
    >
      <span>Case History</span>
      <RouterLink
        v-for="item in caseThreads"
        :key="item.id"
        :to="{ path: '/support', query: { thread: item.id } }"
        :aria-current="item.id === thread ? 'page' : undefined"
      >
        {{ item.status === 'Closed' ? 'Closed' : 'Active' }} · {{ date(item.createdAt) }}
      </RouterLink>
    </nav>
    <div
      v-if="menuOpen && (canCloseConversation || metadata?.canEscalate || metadata?.canAssign)"
      class="conversation-actions w3-container w3-padding-16"
    >
      <button
        v-if="metadata?.canAssign"
        type="button"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="startAction('assign')"
      >
        Assign Conversation
      </button>
      <button
        v-if="canCloseConversation"
        type="button"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="startAction('close')"
      >
        Close Conversation
      </button>
      <button
        v-if="metadata?.canEscalate"
        type="button"
        class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="startAction('escalate')"
      >
        Escalate Conversation
      </button>
    </div>
    <dialog
      ref="actionDialog"
      class="conversation-action-dialog w3-container w3-round-large w3-border w3-padding-large w3-white w3-col s12 m8 l5"
      @close="action = null"
    >
      <div class="w3-bar">
        <form
          v-if="action"
          class="conversation-confirm w3-container w3-padding-16 w3-bar"
          @submit.prevent="completeAction"
        >
          <h3 class="w3-large w3-sans-serif">
            {{
              action === 'close'
                ? 'Close This Conversation?'
                : action === 'assign'
                  ? 'Assign Conversation'
                  : 'Escalate This Conversation?'
            }}
          </h3>
          <p v-if="action === 'close'">
            Closing is permanent. You can still read this conversation.
          </p>
          <label
            v-if="action === 'assign'"
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          >
            Assigned Staff
            <select
              v-model="assignmentTarget"
              class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
            >
              <option value="">Unassigned</option>
              <option
                v-for="person in assignmentChoices"
                :key="person.id"
                :value="person.id"
              >
                {{ person.displayName }}
              </option>
            </select>
          </label>
          <label
            v-if="auth.isStaffWorkspace"
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          >
            Reason
            <textarea
              v-model="actionReason"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              required
              :minlength="portalMetadata.reference.assignmentReason?.minimum"
              :maxlength="portalMetadata.reference.assignmentReason?.maximum ?? undefined"
              rows="3"
            />
          </label>
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="actionBusy"
          >
            {{
              actionBusy
                ? 'Saving…'
                : action === 'close'
                  ? 'Confirm Closure'
                  : action === 'assign'
                    ? 'Confirm Assignment'
                    : 'Confirm Escalation'
            }}
          </button>
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="actionBusy"
            @click="cancelAction"
          >
            Cancel
          </button>
        </form>
      </div>
    </dialog>
    <p
      v-if="cleanRelatedContext"
      class="related-case-context w3-small w3-text-grey"
    >
      {{ cleanRelatedContext }}
    </p>
    <p
      v-if="!online"
      class="offline-form-note w3-panel w3-pale-yellow"
      role="status"
    >
      Offline · your draft stays here.
    </p>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <div
      ref="scroller"
      class="chat-messages w3-container w3-padding-16 w3-content"
      role="log"
      aria-label="Conversation Messages"
      @scroll.passive="onScroll"
    >
      <button
        v-if="olderAvailable"
        type="button"
        class="text-button older-messages w3-button w3-text-black w3-round-large"
        :disabled="olderLoading"
        @click="loadOlder"
      >
        {{ olderLoading ? 'Loading…' : 'Earlier Messages' }}
      </button>
      <button
        v-if="historyMode"
        class="text-button latest-messages w3-button w3-text-black w3-round-large"
        type="button"
        @click="load(true)"
      >
        {{ newMessages ? 'New Messages Available · ' : '' }}Back To Latest
      </button>
      <template
        v-for="message in presentedMessages"
        :key="message.id"
      >
        <p
          v-if="message.startsDay"
          class="chat-day w3-center w3-small w3-text-grey"
        >
          {{ message.dayLabel }}
        </p>
        <div
          v-if="message.sequence === unreadFrom"
          class="unread-divider w3-center w3-small w3-text-blue-grey w3-section"
        >
          New Messages
        </div>
        <div class="w3-row">
          <div
            class="w3-col s10 m9 l7"
            :class="message.authorId === auth.user?.id ? 'w3-right' : 'w3-left'"
          >
            <article
              class="chat-bubble w3-panel w3-round-xlarge w3-padding w3-padding-16"
              :class="
                message.authorId === auth.user?.id
                  ? 'message-own w3-theme-d5 w3-text-white w3-right'
                  : 'w3-white w3-border w3-border-light-grey w3-text-black w3-left'
              "
              :data-message-sequence="message.sequence"
            >
              <strong>
                {{
                  message.authorId === auth.user?.id
                    ? 'You' + (auth.isStaffWorkspace ? ' · ' + (message.authorRole || 'Staff') : '')
                    : auth.isStaffWorkspace
                      ? message.authorName
                      : customerActor(undefined, message.authorRole || message.authorName)
                }}
                <small
                  v-if="
                    auth.isStaffWorkspace &&
                    message.authorRole &&
                    message.authorId !== auth.user?.id
                  "
                  class="w3-small"
                >
                  · {{ message.authorRole }}
                </small>
              </strong>
              <p class="w3-responsive">
                <template
                  v-for="(line, index) in message.bodyLines"
                  :key="index"
                >
                  {{ line }}
                  <br v-if="index < message.bodyLines.length - 1" />
                </template>
              </p>
              <DisclosurePanel
                v-if="message.enquiry"
                class="chat-enquiry-details"
                :inverted="message.authorId === auth.user?.id"
              >
                <template #heading>Enquiry Details</template>
                <div class="w3-responsive">
                  <table class="w3-table w3-bordered">
                    <tbody>
                      <tr>
                        <th
                          class="w3-mobile w3-small"
                          scope="row"
                        >
                          Concern
                        </th>
                        <td class="w3-mobile">{{ message.enquiry.concernLabel }}</td>
                      </tr>
                      <tr v-if="message.enquiry.occurredOn">
                        <th
                          class="w3-mobile w3-small"
                          scope="row"
                        >
                          When It Happened
                        </th>
                        <td class="w3-mobile">{{ message.enquiry.occurredOn }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </DisclosurePanel>
              <time
                class="w3-small w3-block w3-right-align w3-opacity"
                :datetime="message.createdAt"
                :title="message.fullDateLabel"
                :aria-label="message.fullDateLabel"
              >
                {{ message.timeLabel }}
                <span v-if="message.authorId === auth.user?.id">· Sent</span>
              </time>
            </article>
          </div>
        </div>
      </template>
      <div
        v-if="loading && !messages.length"
        class="chat-loading"
        role="status"
        aria-label="Loading Conversation"
      >
        <span class="sr-only w3-hide">Loading Conversation</span>
        <i />
        <i />
        <i />
      </div>
      <div
        v-else-if="!messages.length"
        class="empty-state w3-container w3-center w3-padding-32"
      >
        <MessageCircle :size="26" />
        <p>
          {{
            loading
              ? 'Loading messages…'
              : caseClosed
                ? 'No messages in this conversation.'
                : 'Start The Conversation'
          }}
        </p>
      </div>
    </div>
    <div
      v-if="readOnly"
      class="chat-access w3-container w3-section"
    >
      <CaseClaimAction
        v-if="auth.isStaffWorkspace && ownership && !readonly && !caseClosed && !conversationClosed"
        :item="ownership"
        :staff="currentStaff"
        @claimed="load"
        @refresh="load"
      />
      <p
        v-else
        class="chat-readonly w3-container w3-section"
      >
        {{
          conversationClosed
            ? 'This conversation is permanently closed. Start a new enquiry for further assistance.'
            : caseClosed
              ? 'This dispute is closed. You can read its messages here.'
              : loading
                ? 'Checking access…'
                : 'Read-only conversation'
        }}
      </p>
      <RouterLink
        v-if="(conversationClosed || caseClosed) && newConversationCase && !auth.isStaffWorkspace"
        :to="{ path: '/support', query: { enquiryFor: newConversationCase } }"
        class="button button-secondary chat-new-conversation w3-button w3-white w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
      >
        Start New Conversation
      </RouterLink>
      <button
        v-else-if="conversationClosed && !auth.isStaffWorkspace"
        type="button"
        class="button button-secondary chat-new-conversation w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
        @click="emit('new')"
      >
        Start New Conversation
      </button>
    </div>
    <form
      v-else
      class="chat-composer w3-container w3-white w3-border-top w3-padding-16 w3-bar w3-content"
      @submit.prevent="send"
    >
      <div class="composer-row w3-row-padding">
        <div class="w3-col s8 m10">
          <textarea
            ref="composer"
            v-model="body"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            aria-describedby="chat-security-hint"
            aria-label="Your Message"
            required
            minlength="2"
            :maxlength="portalMetadata.reference.supportBody?.maximum ?? undefined"
            rows="1"
            placeholder="Write a message…"
          />
        </div>
        <div class="w3-col s4 m2">
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding w3-large w3-margin-top"
            :disabled="
              sending ||
              !online ||
              body.trim().length < (portalMetadata.reference.supportBody?.minimum ?? Infinity)
            "
            :aria-label="sending ? 'Sending message' : 'Send Message'"
          >
            {{ sending ? 'Sending…' : 'Send' }}
          </button>
        </div>
      </div>
      <span
        id="chat-security-hint"
        class="sr-only w3-hide"
      >
        Never share passwords, PINs or verification codes.
      </span>
      <small
        v-if="body.length > 1600"
        class="composer-count w3-small"
      >
        {{ body.length }}/2,000
      </small>
    </form>
  </section>
</template>
