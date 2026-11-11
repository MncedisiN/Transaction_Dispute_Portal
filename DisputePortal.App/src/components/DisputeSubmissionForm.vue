<script setup lang="ts">
import { useFeedback } from '../composables/useFeedback'

import { computed, ref } from 'vue'
import { useDisputeInputPolicy } from '../composables/useDisputeInputPolicy'
import { ArrowLeft, ArrowRight, Check } from '@lucide/vue'
import type { Transaction } from '../types'
import { useOnline } from '../composables/useOnline'
import { formatDate, formatMoney } from '../utils/format'

const props = defineProps<{ transaction: Transaction; loading?: boolean }>()
const emit = defineEmits<{
  submit: [payload: { category: string; reason: string; description: string }]
}>()
const { online } = useOnline()
const step = ref<'details' | 'review'>('details')
const category = ref('')
const description = ref('')
const error = ref('')
const { policy, loading: loadingPolicy } = useDisputeInputPolicy()
const categories = computed(() => policy.value?.categories ?? [])
const availableCategories = computed(() =>
  categories.value.filter((option) => option.transactionTypes.includes(props.transaction.type))
)
const reason = computed(
  () => availableCategories.value.find((option) => option.code === category.value)?.reason ?? ''
)
function review() {
  if (!category.value || !reason.value) {
    error.value = 'Choose a reason to continue.'
    return
  }
  if (
    !policy.value ||
    description.value.trim().length < policy.value.descriptionMinimumLength ||
    description.value.trim().length > policy.value.descriptionMaximumLength
  ) {
    error.value = `Please add between ${policy.value?.descriptionMinimumLength} and ${policy.value?.descriptionMaximumLength} characters so we can understand what happened.`
    return
  }
  error.value = ''
  step.value = 'review'
}
useFeedback(error, undefined, undefined)
</script>

<template>
  <section
    class="surface-panel dispute-form-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
  >
    <div class="form-progress w3-bar w3-small w3-text-grey w3-section">
      <span :class="{ 'step-current': step === 'details' }">
        <i>1</i>
        Reason
      </span>
      <span class="progress-line" />
      <span :class="{ 'step-current': step === 'review' }">
        <i>2</i>
        Review
      </span>
    </div>
    <form
      v-if="step === 'details'"
      class="guided-form w3-bar"
      @submit.prevent="review"
    >
      <p
        v-if="loadingPolicy"
        role="status"
      >
        Loading Dispute Options…
      </p>
      <div class="form-section w3-section">
        <fieldset class="dispute-reasons w3-border-0 w3-padding-0 w3-section">
          <legend class="sr-only w3-hide">Dispute Reason</legend>
          <label
            v-for="option in availableCategories"
            :key="option.code"
            class="dispute-reason-option w3-block w3-margin-top w3-text-dark-grey w3-medium"
            :class="{ 'is-selected': category === option.code }"
          >
            <input
              v-model="category"
              class="w3-radio"
              type="radio"
              name="category"
              :value="option.code"
            />
            <span class="dispute-reason-label w3-margin-left">{{ option.label }}</span>
          </label>
        </fieldset>
      </div>
      <div class="form-section w3-section">
        <label
          class="field-label w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="description"
        >
          Add A Few Details
        </label>
        <textarea
          id="description"
          v-model="description"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          rows="4"
          :minlength="policy?.descriptionMinimumLength"
          :maxlength="policy?.descriptionMaximumLength"
          placeholder="Share anything that might help us understand what happened."
          required
        />
        <div class="field-hint w3-small w3-text-grey w3-block">
          <span>Keep it clear and avoid sharing passwords or full card details.</span>
          <span>
            {{ description.length }}/{{ policy?.descriptionMaximumLength.toLocaleString() }}
          </span>
        </div>
      </div>
      <p
        v-if="error"
        class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
        role="alert"
      >
        {{ error }}
      </p>
      <button
        class="button button-primary form-primary-action w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        type="submit"
        :disabled="loadingPolicy || !policy"
      >
        Review Your Dispute
        <ArrowRight :size="17" />
      </button>
    </form>

    <section
      v-else
      class="review-step w3-section"
    >
      <div class="review-intro w3-section">
        <span class="review-check w3-tag w3-theme-d5 w3-round-large w3-padding-small">
          <Check :size="18" />
        </span>
        <div>
          <h2 class="w3-sans-serif w3-large">Check Everything Looks Right</h2>
          <p>We’ll use these details to start your review.</p>
        </div>
      </div>
      <div class="review-transaction w3-panel w3-light-grey w3-padding-large w3-round-xlarge">
        <div class="review-transaction-heading w3-section">
          <small class="w3-small w3-block w3-margin-top">TRANSACTION</small>
          <b class="w3-block w3-margin-top">
            {{ transaction.merchantName || transaction.description }}
          </b>
          <time
            class="w3-block w3-margin-top"
            :datetime="transaction.transactionAt"
          >
            {{ formatDate(transaction.transactionAt) }}
          </time>
        </div>
        <div class="review-transaction-facts w3-section">
          <div class="review-account w3-section">
            <small class="w3-small w3-block w3-margin-top">
              {{ transaction.accountType ? transaction.accountType + ' account' : 'Account' }}
            </small>
            <b class="review-account-number w3-block w3-margin-top">
              {{ transaction.accountNumber || 'Not available' }}
            </b>
          </div>
          <div class="review-disputed-amount w3-section">
            <small class="w3-small w3-block w3-margin-top">DISPUTED AMOUNT</small>
            <strong class="w3-block w3-margin-top">
              {{ formatMoney(transaction.amount, transaction.currency) }}
            </strong>
          </div>
        </div>
      </div>
      <div class="review-answer w3-section">
        <small class="w3-small w3-block w3-margin-top">YOUR CONCERN</small>
        <b class="w3-block w3-margin-top">
          {{ categories.find((item) => item.code === category)?.label }}
        </b>
        <p>{{ description }}</p>
      </div>
      <p
        v-if="!online"
        class="offline-form-note w3-panel w3-pale-yellow"
        role="status"
      >
        Reconnect to securely submit your dispute.
      </p>
      <div class="review-actions w3-row-padding w3-section">
        <div class="w3-col s12 m6 w3-margin-bottom">
          <button
            class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
            type="button"
            @click="step = 'details'"
          >
            <ArrowLeft
              class="w3-margin-right"
              :size="16"
            />
            Edit Details
          </button>
        </div>
        <div class="w3-col s12 m6 w3-margin-bottom">
          <button
            class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
            type="button"
            :disabled="loading || !online"
            @click="emit('submit', { category, reason, description: description.trim() })"
          >
            {{ loading ? 'Submitting…' : online ? 'Submit Dispute' : 'Offline' }}
            <ArrowRight :size="17" />
          </button>
        </div>
      </div>
    </section>
  </section>
</template>
