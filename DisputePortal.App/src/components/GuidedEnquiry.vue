<script setup lang="ts">
import { reference } from '../portalReference'
import { clientOption } from '../utils/portalContent'
import { computed, nextTick, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ArrowLeft, ArrowRight, Check, Pencil, Send, ShieldCheck } from '@lucide/vue'
import { api, ApiError } from '../api'
import { useAuthStore } from '../stores/auth'
import { useFeedback } from '../composables/useFeedback'
import { useEnquiryFlow } from '../composables/useEnquiryFlow'
import { useEnquiryLookup } from '../composables/useEnquiryLookup'
import { useOnline } from '../composables/useOnline'
import { useAutosizeTextarea } from '../composables/useAutosizeTextarea'
import { requestId } from '../utils/requestId'
import { notify } from '../toasts'
import { formatDate, formatMoney } from '../utils/format'
import {
  contextKind,
  enquiryBody,
  enquiryProblem,
  enquirySubmission,
  type EnquiryDraft
} from '../utils/enquiryDraft'
import StatusBadge from './StatusBadge.vue'
import type { Profile, SupportCaseOption, SupportConcern } from '../operations'
import type { Transaction } from '../types'

const props = defineProps<{
  backTo: string
  initialCase?: SupportCaseOption | null
  initialTransaction?: Transaction | null
}>()
const emit = defineEmits<{ sent: [id: string] }>()
const auth = useAuthStore(),
  { online } = useOnline()
const draft = reactive<EnquiryDraft>({
  requestId: requestId(),
  concernCode: '',
  relatedCase: props.initialCase ?? null,
  transaction: props.initialTransaction ?? null,
  description: '',
  additionalDetails: '',
  occurredOn: '',
  additionalAnswered: false,
  dateAnswered: false,
  noTransaction: false
})
const concerns = ref<SupportConcern[]>([]),
  firstName = ref(''),
  loading = ref(true),
  busy = ref(false),
  error = ref(''),
  uncertain = ref(false)
const concern = computed(() => concerns.value.find((value) => value.code === draft.concernCode))
const { step, phase, prompt, goTo, nextDetails } = useEnquiryFlow(
  draft,
  concern,
  !!props.initialCase
)
const displayName = computed(() => firstName.value || auth.user?.displayName || 'there')
const scroller = ref<HTMLElement | null>(null),
  textarea = ref<HTMLTextAreaElement | null>(null)
const textInput = computed({
  get: () => (step.value === 'additional' ? draft.additionalDetails : draft.description),
  set: (value) => {
    if (step.value === 'additional') draft.additionalDetails = value
    else draft.description = value
  }
})
useAutosizeTextarea(textarea, textInput)
const textStep = computed(() => step.value === 'description' || step.value === 'additional')
const problem = computed(() => enquiryProblem(draft, concern.value))
const { search, lookupBusy, transactions, cases, page, total, lookup, loadNextPage } =
  useEnquiryLookup(step, error)
let disposed = false
let pending: ReturnType<typeof enquirySubmission> | undefined
let initialization: AbortController | undefined
async function init() {
  initialization?.abort()
  const request = new AbortController()
  initialization = request
  const timer = setTimeout(() => request.abort(), clientOption('requestTimeoutMilliseconds'))
  loading.value = true
  error.value = ''
  try {
    const [catalog, profile] = await Promise.all([
      api.get<SupportConcern[]>('/api/v1/support/concerns', request.signal),
      api.get<Profile>('/api/v1/profile', request.signal).catch(() => null)
    ])
    if (disposed || initialization !== request) return
    if (!catalog.length) throw new Error('No enabled concern types')
    concerns.value = catalog
    firstName.value = profile?.firstName ?? ''
  } catch {
    if (!disposed && initialization === request)
      error.value = 'Your enquiry could not be opened. Please retry.'
  } finally {
    clearTimeout(timer)
    if (!disposed && initialization === request) loading.value = false
  }
}
onMounted(() => {
  void init()
})
onUnmounted(() => {
  disposed = true
  initialization?.abort()
})
watch(step, async () => {
  error.value = ''
  await nextTick()
  if (scroller.value) scroller.value.scrollTop = scroller.value.scrollHeight
})
function skipTransaction() {
  draft.noTransaction = true
  nextDetails()
}
async function chooseConcern(value: SupportConcern) {
  if (draft.concernCode !== value.code) draft.noTransaction = false
  draft.concernCode = value.code
  const kind = contextKind(value)
  if ((value.requiresCase || kind === 'Case') && !draft.relatedCase) {
    await showPicker('case')
    return
  }
  if (kind === 'Transaction' && !draft.transaction && !draft.relatedCase) {
    await showPicker('transaction')
    return
  }
  if (
    kind === 'OptionalTransaction' &&
    !draft.transaction &&
    !draft.relatedCase &&
    !draft.noTransaction
  ) {
    goTo('optional-transaction')
    return
  }
  nextDetails()
}
function chooseRelation(keep: boolean) {
  if (!keep) {
    draft.relatedCase = null
    draft.transaction = null
  }
  goTo('concern')
}
async function showPicker(kind: 'transaction' | 'case') {
  goTo(kind)
  search.value = ''
  page.value = 1
  transactions.value = []
  cases.value = []
  await lookup()
}
function selectTransaction(value: Transaction) {
  draft.transaction = value
  draft.relatedCase = null
  draft.noTransaction = false
  nextDetails()
}
function selectCase(value: SupportCaseOption) {
  draft.relatedCase = value
  draft.transaction = null
  nextDetails()
}
function continueText() {
  error.value = ''
  if (step.value === 'description') {
    if (draft.description.trim().length < (reference.supportBody?.minimum ?? Infinity)) {
      error.value = 'Tell us what you need help with.'
      return
    }
  }
  if (enquiryBody(draft).length > (reference.supportBody?.maximum ?? Infinity)) {
    error.value = `Keep your complete enquiry within ${reference.supportBody?.maximum?.toLocaleString()} characters.`
    return
  }
  if (step.value === 'additional') draft.additionalAnswered = true
  nextDetails()
}
function skipAdditional() {
  draft.additionalDetails = ''
  draft.additionalAnswered = true
  goTo('review')
}
function finishDate(skip = false) {
  if (skip) draft.occurredOn = ''
  draft.dateAnswered = true
  nextDetails()
}
function editDetails() {
  if (uncertain.value || busy.value) return
  pending = undefined
  goTo('description')
}
async function submit() {
  if (busy.value || !online.value || !concern.value) return
  error.value = ''
  if (problem.value) {
    error.value = problem.value
    return
  }
  pending ??= enquirySubmission(draft, concern.value)
  busy.value = true
  try {
    const result = await api.post<{ id: string }>('/api/v1/support', pending)
    if (!disposed) {
      notify('Your Enquiry Has Been Sent.', 'success')
      emit('sent', result.id)
    }
  } catch (cause) {
    if (disposed) return
    uncertain.value = !(
      cause instanceof ApiError && [400, 401, 403, 404, 409, 422].includes(cause.status)
    )
    if (!uncertain.value) pending = undefined
    error.value = uncertain.value
      ? 'Your request may still be processing. Retry to confirm the same enquiry.'
      : (cause as Error).message
  } finally {
    if (!disposed) busy.value = false
  }
}
useFeedback(error, undefined, undefined)
</script>

<template>
  <section
    class="chat-panel guided-enquiry w3-overlay w3-show w3-theme-l5 w3-text-black w3-responsive"
    aria-label="Guided Enquiry"
  >
    <header
      class="chat-heading guided-heading w3-content w3-container w3-white w3-border-bottom w3-padding-16 w3-bar"
    >
      <RouterLink
        :to="backTo"
        class="icon-button w3-button w3-round-large w3-left w3-margin-right"
        aria-label="Back To Messages"
      >
        <ArrowLeft :size="20" />
      </RouterLink>
      <div class="w3-rest">
        <span class="guided-draft w3-right w3-small w3-text-grey">Draft · {{ phase }}</span>
        <h2 class="w3-large w3-sans-serif"><b>Dispute Portal Support</b></h2>
        <p class="w3-small w3-text-grey">New Enquiry</p>
      </div>
    </header>
    <p
      v-if="!online"
      class="chat-readonly w3-container w3-section"
      role="status"
    >
      You Are Offline. Your Draft Stays Here.
    </p>
    <div
      ref="scroller"
      class="chat-messages guided-messages w3-container w3-padding-16 w3-content"
      role="log"
      aria-label="Enquiry Conversation"
    >
      <div class="w3-row">
        <div class="w3-col s10 m9 l7 w3-left">
          <article
            class="chat-bubble guided-assistant w3-panel w3-round-xlarge w3-padding w3-padding-16 w3-white w3-border w3-border-light-grey w3-left"
          >
            <p>Hi {{ displayName }}! How can we help you today?</p>
          </article>
        </div>
      </div>
      <p
        v-if="loading"
        class="guided-loading w3-center w3-padding-32"
        role="status"
      >
        Opening your enquiry…
      </p>
      <template v-else>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-right">
            <article
              v-if="concern"
              class="chat-bubble message-own w3-panel w3-round-xlarge w3-text-white w3-right w3-padding-large w3-theme-d5"
            >
              <p>{{ concern.label }}</p>
              <button
                type="button"
                class="guided-edit w3-button w3-small w3-text-white w3-round-large"
                :disabled="busy || uncertain"
                @click="goTo('concern')"
              >
                <Pencil
                  class="w3-margin-right"
                  :size="13"
                />
                Change Concern
              </button>
            </article>
          </div>
        </div>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-right">
            <article
              v-if="draft.relatedCase"
              class="chat-bubble message-own guided-context w3-panel w3-round-xlarge w3-text-white w3-right w3-padding-large w3-theme-d5"
            >
              <strong>{{ draft.relatedCase.reference }}</strong>
              <p>
                {{ draft.relatedCase.merchantName || draft.relatedCase.transactionDescription }}
              </p>
              <StatusBadge :status="draft.relatedCase.status" />
              <button
                v-if="step !== 'relation'"
                type="button"
                class="guided-edit w3-button w3-small w3-text-white w3-round-large"
                :disabled="busy || uncertain"
                @click="showPicker('case')"
              >
                Change Case
              </button>
            </article>
          </div>
        </div>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-right">
            <article
              v-if="draft.transaction"
              class="chat-bubble message-own guided-context w3-panel w3-round-xlarge w3-text-white w3-right w3-padding-large w3-theme-d5"
            >
              <strong>{{ draft.transaction.merchantName || draft.transaction.description }}</strong>
              <p>
                {{ draft.transaction.typeLabel || draft.transaction.type }} ·
                {{ formatMoney(draft.transaction.amount, draft.transaction.currency) }}
              </p>
              <small class="w3-small">{{ formatDate(draft.transaction.transactionAt) }}</small>
              <button
                type="button"
                class="guided-edit w3-button w3-small w3-text-white w3-round-large"
                :disabled="busy || uncertain"
                @click="showPicker('transaction')"
              >
                Change Transaction
              </button>
            </article>
          </div>
        </div>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-left">
            <article
              v-if="draft.description && step !== 'description' && step !== 'review'"
              class="chat-bubble guided-assistant w3-panel w3-round-xlarge w3-padding w3-padding-16 w3-white w3-border w3-border-light-grey w3-left"
            >
              <p>Tell us what happened and how we can help.</p>
            </article>
          </div>
        </div>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-right">
            <article
              v-if="draft.description && step !== 'description' && step !== 'review'"
              class="chat-bubble message-own w3-panel w3-round-xlarge w3-text-white w3-right w3-padding-large w3-theme-d5"
            >
              <p>{{ draft.description }}</p>
              <button
                type="button"
                class="guided-edit w3-button w3-small w3-text-white w3-round-large"
                :disabled="busy || uncertain"
                @click="editDetails"
              >
                Edit Details
              </button>
            </article>
          </div>
        </div>
        <template v-if="draft.additionalAnswered && step !== 'additional' && step !== 'review'">
          <div class="w3-row">
            <div class="w3-col s10 m9 l7 w3-left">
              <article
                class="chat-bubble guided-assistant w3-panel w3-round-xlarge w3-padding w3-padding-16 w3-white w3-border w3-border-light-grey w3-left"
              >
                <p>Is there anything else you would like our team to know?</p>
              </article>
            </div>
          </div>
          <div class="w3-row">
            <div class="w3-col s10 m9 l7 w3-right">
              <article
                class="chat-bubble message-own w3-panel w3-round-xlarge w3-text-white w3-right w3-padding-large w3-theme-d5"
              >
                <p>{{ draft.additionalDetails || 'Nothing else to add.' }}</p>
              </article>
            </div>
          </div>
        </template>
        <div class="w3-row">
          <div class="w3-col s10 m9 l7 w3-left">
            <article
              class="chat-bubble guided-assistant w3-panel w3-round-xlarge w3-padding w3-padding-16 w3-white w3-border w3-border-light-grey w3-left"
            >
              <p>{{ prompt }}</p>
            </article>
          </div>
        </div>
        <div
          v-if="step === 'relation'"
          class="guided-choices w3-section"
        >
          <button
            class="guided-choice w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
            @click="chooseRelation(true)"
          >
            About This Case
            <ArrowRight :size="16" />
          </button>
          <button
            class="guided-choice w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
            @click="chooseRelation(false)"
          >
            Something Else
            <ArrowRight :size="16" />
          </button>
        </div>
        <div
          v-if="step === 'concern'"
          class="guided-choices w3-section"
          aria-label="Concern Options"
        >
          <button
            v-for="item in concerns"
            :key="item.code"
            :class="{ 'w3-light-grey': (draft.concernCode === item.code) === true }"
            class="guided-choice w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
            :aria-pressed="draft.concernCode === item.code"
            @click="chooseConcern(item)"
          >
            {{ item.label }}
            <Check
              v-if="draft.concernCode === item.code"
              :size="17"
            />
            <ArrowRight
              v-else
              :size="16"
            />
          </button>
        </div>
        <div
          v-if="step === 'optional-transaction'"
          class="guided-choices w3-section"
        >
          <button
            class="guided-choice w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
            @click="showPicker('transaction')"
          >
            Choose A Transaction
            <ArrowRight :size="16" />
          </button>
          <button
            class="guided-choice w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
            @click="skipTransaction"
          >
            It Is About Something Else
            <ArrowRight :size="16" />
          </button>
        </div>
        <section
          v-if="step === 'transaction' || step === 'case'"
          class="guided-picker w3-section"
          :aria-label="step === 'case' ? 'Select Related Case' : 'Select Related Transaction'"
        >
          <form
            class="guided-search w3-bar"
            role="search"
            @submit.prevent="lookup()"
          >
            <input
              v-model="search"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="search"
              maxlength="100"
              :aria-label="step === 'case' ? 'Find A Case' : 'Find A Transaction'"
              :placeholder="step === 'case' ? 'Case Reference' : 'Merchant Or Description'"
            />
            <button
              type="submit"
              class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              :disabled="lookupBusy"
            >
              Search
            </button>
          </form>
          <p
            v-if="lookupBusy"
            role="status"
          >
            Loading…
          </p>
          <template v-else-if="step === 'transaction'">
            <button
              v-for="item in transactions"
              :key="item.id"
              class="guided-choice guided-selection w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
              @click="selectTransaction(item)"
            >
              <span>
                <strong>{{ item.merchantName || item.description }}</strong>
                <small class="w3-small">
                  {{ item.typeLabel || item.type }} · {{ formatDate(item.transactionAt) }}
                </small>
              </span>
              <span>
                {{ formatMoney(item.amount, item.currency) }}
                <ArrowRight :size="16" />
              </span>
            </button>
            <p
              v-if="!transactions.length"
              role="status"
            >
              No Matching Transactions. Try Another Search.
            </p>
          </template>
          <template v-else>
            <button
              v-for="item in cases"
              :key="item.id"
              class="guided-choice guided-selection w3-button w3-block w3-white w3-border w3-round-large w3-padding-16 w3-margin-bottom w3-left-align"
              @click="selectCase(item)"
            >
              <span>
                <strong>{{ item.reference }}</strong>
                <small class="w3-small">
                  {{ item.merchantName || item.transactionDescription }}
                </small>
              </span>
              <span>
                <StatusBadge :status="item.status" />
                <ArrowRight :size="16" />
              </span>
            </button>
            <p
              v-if="!cases.length"
              role="status"
            >
              No Matching Cases. Try Another Reference.
            </p>
          </template>
          <button
            v-if="(step === 'transaction' ? transactions.length : cases.length) < total"
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="lookupBusy"
            @click="loadNextPage"
          >
            Load More
          </button>
          <button
            type="button"
            class="text-button w3-button w3-text-black w3-round-large"
            @click="goTo('concern')"
          >
            Back To Concerns
          </button>
        </section>
        <section
          v-if="step === 'date'"
          class="guided-date w3-section"
        >
          <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
            When It Happened
            <input
              v-model="draft.occurredOn"
              class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
              type="date"
            />
          </label>
          <div class="guided-choices w3-section">
            <button
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="!draft.occurredOn"
              @click="finishDate()"
            >
              Continue
            </button>
            <button
              class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
              @click="finishDate(true)"
            >
              I Am Not Sure
            </button>
          </div>
        </section>
        <section
          v-if="step === 'review'"
          class="guided-summary w3-container w3-light-grey w3-padding-16"
          aria-label="Review Your Enquiry"
        >
          <h3 class="w3-large w3-sans-serif">Your Enquiry</h3>
          <div class="w3-responsive">
            <table class="w3-table w3-bordered">
              <tbody>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Concern
                  </th>
                  <td class="w3-mobile">{{ concern?.label }}</td>
                </tr>
                <tr v-if="draft.relatedCase">
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Related Case
                  </th>
                  <td class="w3-mobile">{{ draft.relatedCase.reference }}</td>
                </tr>
                <tr v-if="draft.transaction">
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Transaction
                  </th>
                  <td class="w3-mobile">
                    {{ draft.transaction.merchantName || draft.transaction.description }} ·
                    {{ formatMoney(draft.transaction.amount, draft.transaction.currency) }}
                  </td>
                </tr>
                <tr>
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Description
                  </th>
                  <td class="w3-mobile">{{ draft.description }}</td>
                </tr>
                <tr v-if="draft.additionalDetails.trim()">
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    Additional Details
                  </th>
                  <td class="w3-mobile">{{ draft.additionalDetails }}</td>
                </tr>
                <tr v-if="draft.occurredOn && concern?.collectDate">
                  <th
                    class="w3-mobile w3-text-grey w3-small"
                    scope="row"
                  >
                    When It Happened
                  </th>
                  <td class="w3-mobile">{{ draft.occurredOn }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p>Only Submit When These Details Are Correct.</p>
          <button
            type="button"
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            :disabled="busy || uncertain"
            @click="editDetails"
          >
            Edit Details
          </button>
          <button
            type="button"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            :disabled="busy || !online || !!problem"
            @click="submit"
          >
            <Send
              class="w3-margin-right"
              :size="16"
            />
            {{ busy ? 'Submitting…' : uncertain ? 'Retry Submission' : 'Submit Enquiry' }}
          </button>
        </section>
      </template>
      <div
        v-if="error"
        class="guided-error w3-panel w3-pale-red"
        role="alert"
      >
        <p>{{ error }}</p>
        <button
          v-if="!concerns.length"
          class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
          @click="init"
        >
          Retry
        </button>
      </div>
    </div>
    <form
      v-if="textStep && !loading"
      class="chat-composer guided-composer w3-container w3-white w3-border-top w3-padding-16 w3-bar w3-content"
      @submit.prevent="continueText"
    >
      <label
        class="sr-only w3-block w3-margin-top w3-hide w3-small w3-text-dark-grey"
        for="guided-message"
      >
        Your Message
      </label>
      <div class="composer-row w3-row-padding">
        <div class="w3-col s8 m10">
          <textarea
            id="guided-message"
            ref="textarea"
            v-model="textInput"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            rows="2"
            :maxlength="
              (step === 'additional'
                ? reference.additionalDetails?.maximum
                : reference.supportBody?.maximum) ?? undefined
            "
            :placeholder="
              step === 'additional' ? 'Any Additional Details…' : 'Tell Us What Happened…'
            "
            :disabled="busy"
          />
        </div>
        <div class="w3-col s4 m2">
          <button
            type="submit"
            aria-label="Continue"
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding w3-large w3-margin-top"
            :disabled="
              busy ||
              (step === 'description' &&
                textInput.trim().length < (reference.supportBody?.minimum ?? Infinity))
            "
          >
            <ArrowRight :size="18" />
            <span class="sr-only w3-hide">Continue</span>
          </button>
        </div>
      </div>
      <div class="guided-composer-note w3-small w3-text-grey">
        <small class="w3-small">Never Share Passwords, PINs Or Full Card Numbers.</small>
        <button
          v-if="step === 'additional'"
          class="text-button w3-button w3-text-black w3-round-large"
          type="button"
          @click="skipAdditional"
        >
          Nothing Else
        </button>
      </div>
    </form>
    <footer
      v-else
      class="guided-footer w3-container w3-padding-16"
    >
      <ShieldCheck :size="14" />
      <span>Your enquiry is sent only after you confirm.</span>
    </footer>
  </section>
</template>
