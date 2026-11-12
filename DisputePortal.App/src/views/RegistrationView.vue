<script setup lang="ts">
import { requestPushPermissionOnSignIn } from '../pushNotifications'
import AuthLayout from '../components/AuthLayout.vue'
import { reference } from '../portalReference'
import { formatDate } from '../utils/format'
import { useFeedback } from '../composables/useFeedback'

import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { phoneCountries as countryChoices, normalizePhoneNumber } from '../utils/phoneNumbers'
import { ShieldCheck, ArrowRight } from '@lucide/vue'
import CountryPicker from '../components/CountryPicker.vue'
import PasswordInput from '../components/PasswordInput.vue'
import BackLink from '../components/BackLink.vue'
import { api, ApiError } from '../api'
import { loadPlatform, platform } from '../platform'
import { useAuthStore, type IdentityChallenge } from '../stores/auth'
import { readVerification, saveVerification, clearVerification } from '../utils/pendingVerification'
const router = useRouter(),
  auth = useAuthStore(),
  step = ref(1),
  busy = ref(false),
  error = ref(''),
  channels = ref<string[]>([]),
  verificationRequired = ref(true)
const firstName = ref(''),
  lastName = ref(''),
  email = ref(''),
  phone = ref(''),
  phoneCountry = ref(''),
  countryCode = ref(''),
  channel = ref('email'),
  documentIssuer = ref(''),
  documentType = ref(''),
  documentNumber = ref(''),
  dateOfBirth = ref(''),
  password = ref(''),
  confirmation = ref(''),
  code = ref('')
const saved = readVerification('registration')
const challenge = ref<IdentityChallenge | null>(
  saved
    ? {
        challengeId: saved.id,
        expiresAt: saved.expiresAt,
        maskedDestination: saved.maskedDestination,
        channel: saved.channel ?? 'email',
        requiresVerification: true
      }
    : null
)
const countries = computed(() => platform.settings?.countries.filter((c) => c.active) ?? []),
  issuer = computed(() => countries.value.find((c) => c.code === documentIssuer.value)),
  document = computed(() => issuer.value?.documents.find((d) => d.key === documentType.value))
const phoneCountries = countryChoices(navigator.language)
watch(documentIssuer, () => {
  documentType.value = issuer.value?.documents[0]?.key ?? ''
  documentNumber.value = ''
})
async function load() {
  error.value = ''
  try {
    const [settings, delivery] = await Promise.all([
      loadPlatform(true),
      api.get<{ channels: string[]; verificationRequired?: boolean }>('/api/v1/identity/channels')
    ])
    countryCode.value = settings.operatingCountry
    phoneCountry.value = settings.operatingCountry
    documentIssuer.value = settings.operatingCountry
    verificationRequired.value = delivery.verificationRequired !== false
    channels.value = delivery.channels
    channel.value = channels.value[0] ?? 'email'
  } catch (e) {
    error.value =
      e instanceof ApiError
        ? e.message
        : 'Registration is temporarily unavailable. Please try again.'
  }
}
onMounted(load)
function phoneValue() {
  return normalizePhoneNumber(phone.value, phoneCountry.value)
}
function next() {
  error.value = ''
  try {
    phoneValue()
    step.value = 2
  } catch (e) {
    error.value = (e as Error).message
  }
}
async function submit() {
  if (busy.value) return
  busy.value = true
  error.value = ''
  void requestPushPermissionOnSignIn()
  try {
    if (challenge.value) {
      await api.post('/api/v1/identity/register/verify', {
        challengeId: challenge.value.challengeId,
        code: code.value
      })
      clearVerification('registration')
      await auth.loadAuthenticatedSession()
      await router.replace('/')
      return
    }
    if (password.value !== confirmation.value) throw new Error('Passwords must match.')
    const mobile = phoneValue()
    const contact = channel.value === 'email' ? email.value : mobile
    const result = await api.post<
      IdentityChallenge | { authenticated: boolean; requiresVerification: false }
    >('/api/v1/identity/register', {
      firstName: firstName.value,
      lastName: lastName.value,
      contact,
      channel: channel.value,
      password: password.value,
      details: {
        countryCode: countryCode.value,
        email: email.value,
        phone: mobile,
        phoneCountry: phoneCountry.value,
        documentType: documentType.value,
        documentIssuer: documentIssuer.value,
        documentNumber: documentNumber.value,
        dateOfBirth:
          document.value?.validator === 'south-african-id' ? null : dateOfBirth.value || null
      }
    })
    if (result.requiresVerification === false) {
      clearVerification('registration')
      if (result.authenticated) await auth.loadAuthenticatedSession()
      await router.replace(result.authenticated ? '/' : '/sign-in')
      return
    }
    challenge.value = result
    saveVerification('registration', {
      id: challenge.value.challengeId,
      expiresAt: challenge.value.expiresAt,
      maskedDestination: challenge.value.maskedDestination,
      channel: challenge.value.channel
    })
    password.value = ''
    confirmation.value = ''
    documentNumber.value = ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Please check your connection and try again.'
  } finally {
    busy.value = false
  }
}
function startAgain() {
  challenge.value = null
  clearVerification('registration')
}
useFeedback(error, undefined, undefined)
</script>
<template>
  <AuthLayout>
    <BackLink
      fallback="/sign-in"
      label="Back To Sign-In"
    />

    <h2 class="w3-center w3-sans-serif w3-large">
      <b>
        {{
          challenge
            ? 'Verify It Is You'
            : step === 1
              ? 'Let’s Get You Started'
              : 'A Few Final Details'
        }}
      </b>
    </h2>
    <p class="auth-intro w3-center w3-small w3-text-grey w3-margin-bottom">
      {{
        challenge
          ? 'Enter the code sent to ' + challenge.maskedDestination + '.'
          : step === 1
            ? 'Your details, wherever you call home.'
            : 'Confirm your identity and secure your account.'
      }}
    </p>
    <p
      v-if="error"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
      role="alert"
    >
      {{ error }}
    </p>
    <button
      v-if="!platform.settings"
      class="button w3-button w3-round-large"
      @click="load"
    >
      Try Again
    </button>
    <form
      v-if="platform.settings"
      class="auth-form registration-form"
      @submit.prevent="challenge || step === 2 ? submit() : next()"
    >
      <template v-if="!challenge">
        <div
          class="registration-steps w3-row w3-section"
          aria-label="Registration Progress"
        >
          <span
            class="w3-col s6 w3-padding-16 w3-small"
            :class="{ 'current w3-border-bottom w3-border-black': step === 1 }"
          >
            1 · Your details
          </span>
          <span
            class="w3-col s6 w3-padding-16 w3-small"
            :class="{ 'current w3-border-bottom w3-border-black': step === 2 }"
          >
            2 · Secure your account
          </span>
        </div>
        <template v-if="step === 1">
          <div class="registration-names">
            <div>
              <label
                class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
                for="reg-first"
              >
                First Name
              </label>
              <input
                id="reg-first"
                v-model.trim="firstName"
                class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                required
                maxlength="100"
                autocomplete="given-name"
              />
            </div>
            <div>
              <label
                class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
                for="reg-last"
              >
                Surname
              </label>
              <input
                id="reg-last"
                v-model.trim="lastName"
                class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                required
                maxlength="100"
                autocomplete="family-name"
              />
            </div>
          </div>
          <CountryPicker
            id="reg-residence"
            v-model="countryCode"
            :countries="countries"
            label="Country Of Residence"
          />
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-email"
          >
            Email Address
          </label>
          <input
            id="reg-email"
            v-model.trim="email"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            type="email"
            required
            maxlength="320"
            autocomplete="email"
          />
          <CountryPicker
            id="reg-phone-country"
            v-model="phoneCountry"
            :countries="phoneCountries"
            label="Mobile Country Code"
            dial
          />
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-phone"
          >
            Mobile Number
          </label>
          <input
            id="reg-phone"
            v-model.trim="phone"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            type="tel"
            required
            maxlength="30"
            autocomplete="tel-national"
          />
          <p class="auth-hint w3-small w3-text-grey w3-section">
            Your mobile country code can differ from your country of residence.
          </p>
        </template>
        <template v-else>
          <CountryPicker
            id="reg-issuer"
            v-model="documentIssuer"
            :countries="countries"
            label="Identity Document Issued By"
          />
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-doc-type"
          >
            Identity Document
          </label>
          <select
            id="reg-doc-type"
            v-model="documentType"
            class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
            required
          >
            <option
              v-for="item in issuer?.documents"
              :key="item.key"
              :value="item.key"
            >
              {{ item.label }}
            </option>
          </select>
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-doc-number"
          >
            {{ document?.label ?? 'Document' }} Number
          </label>
          <input
            id="reg-doc-number"
            v-model.trim="documentNumber"
            class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
            required
            maxlength="100"
            autocomplete="off"
          />
          <label
            v-if="verificationRequired"
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-method"
          >
            Send Verification Code By
          </label>
          <select
            v-if="verificationRequired"
            id="reg-method"
            v-model="channel"
            class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
            required
          >
            <option
              v-for="method in channels"
              :key="method"
              :value="method"
            >
              {{ method === 'email' ? 'Email' : method === 'sms' ? 'SMS' : 'WhatsApp' }}
            </option>
          </select>
          <p
            v-if="!channels.length"
            class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
          >
            Verification is temporarily unavailable. Please try again shortly.
          </p>
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-password"
          >
            Create A Password
          </label>
          <PasswordInput
            id="reg-password"
            v-model="password"
            required
            :minlength="reference.password?.minimum"
            :maxlength="reference.password?.maximum ?? undefined"
            autocomplete="new-password"
          />
          <label
            class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
            for="reg-confirm"
          >
            Confirm Password
          </label>
          <PasswordInput
            id="reg-confirm"
            v-model="confirmation"
            required
            :minlength="reference.password?.minimum"
            :maxlength="reference.password?.maximum ?? undefined"
            autocomplete="new-password"
          />
          <p class="auth-hint w3-small w3-text-grey w3-section">
            At least {{ reference.password?.minimum }}
            characters, with upper and lower case letters, a number and a symbol.
          </p>
          <button
            type="button"
            class="text-link w3-text-black w3-button w3-round-large"
            @click="step = 1"
          >
            Back To Your Details
          </button>
        </template>
      </template>
      <template v-else>
        <label
          class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
          for="reg-code"
        >
          Six-Digit Verification Code
        </label>
        <input
          id="reg-code"
          v-model.trim="code"
          class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
          inputmode="numeric"
          autocomplete="one-time-code"
          pattern="[0-9]{6}"
          maxlength="6"
          required
        />
        <p class="auth-hint w3-small w3-text-grey w3-section">
          Your code expires at
          {{
            challenge
              ? formatDate(challenge.expiresAt, { hour: '2-digit', minute: '2-digit' })
              : '—'
          }}.
        </p>
        <button
          type="button"
          class="text-link w3-text-black w3-button w3-round-large"
          @click="startAgain"
        >
          Start Again
        </button>
      </template>
      <button
        class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
        :disabled="busy || (!challenge && step === 2 && !channels.length)"
      >
        {{
          busy
            ? 'Please Wait…'
            : challenge
              ? 'Verify And Continue'
              : step === 1
                ? 'Continue'
                : 'Create Account Securely'
        }}
        <ArrowRight :size="18" />
      </button>
      <p class="auth-legal registration-security w3-small w3-section">
        <ShieldCheck
          class="w3-margin-right"
          :size="16"
        />
        <span>Your identity details are encrypted. We never send passwords in messages.</span>
      </p>
    </form>
  </AuthLayout>
</template>
