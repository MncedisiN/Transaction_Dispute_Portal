<script setup lang="ts">
import DisclosurePanel from '../components/DisclosurePanel.vue'
import { reference } from '../portalReference'
import { useFeedback } from '../composables/useFeedback'

import CountryPicker from '../components/CountryPicker.vue'
import { phoneCountries as countryChoices, normalizePhoneNumber } from '../utils/phoneNumbers'
import { loadPlatform, platform } from '../platform'
import RememberedDevices from '../components/RememberedDevices.vue'
import PasswordChangePanel from '../components/PasswordChangePanel.vue'
import PasswordInput from '../components/PasswordInput.vue'
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '../api'
import { useAuthStore } from '../stores/auth'
import type { Profile, ResidentialAddress } from '../operations'
import { formatDate, formatBirthDate } from '../utils/format'
import NotificationPreferences from '../components/NotificationPreferences.vue'
import SecurityActivity from '../components/SecurityActivity.vue'
import AccountDeletionPanel from '../components/AccountDeletionPanel.vue'
import { readVerification, saveVerification, clearVerification } from '../utils/pendingVerification'
import IdentityNumberControl from '../components/IdentityNumberControl.vue'
import SignOutAction from '../components/SignOutAction.vue'
import AppShell from '../components/AppShell.vue'
import PageHeading from '../components/PageHeading.vue'
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const activeTab = ref<'details' | 'security' | 'alerts'>(
  route.query.section === 'security'
    ? 'security'
    : route.query.section === 'alerts'
      ? 'alerts'
      : 'details'
)
const contactDisclosure = ref<InstanceType<typeof DisclosurePanel> | null>(null)
let restoringContact = false
watch(activeTab, (value) => {
  void router.replace({
    query: { ...route.query, section: value === 'details' ? undefined : value }
  })
})
const profile = ref<Profile | null>(null)
const firstName = ref('')
const lastName = ref('')
const middleName = ref('')
const address = ref<ResidentialAddress>({
  line1: '',
  line2: '',
  suburb: '',
  city: '',
  province: '',
  postalCode: '',
  countryCode: ''
})
const error = ref('')
const success = ref('')
const busy = ref(false)
const field = ref('email')
const destination = ref('')
const phoneCountry = ref('')
const phoneCountries = countryChoices(navigator.language)
const channel = ref('email')
const password = ref('')
const code = ref('')
const channels = ref<string[]>([])
const challenge = ref<{ id: string; maskedDestination: string; expiresAt: string } | null>(null)
onMounted(async () => {
  try {
    await loadPlatform()
    profile.value = await api.get<Profile>('/api/v1/profile')
    phoneCountry.value = platform.settings?.operatingCountry ?? ''
    documentIssuer.value =
      profile.value.identityIssuerCountry ??
      profile.value.residenceCountry ??
      platform.settings?.operatingCountry ??
      ''
    documentType.value =
      profile.value.identityDocumentType ?? issuerCountry.value?.documents[0]?.key ?? ''
    if (!profile.value.address)
      address.value.countryCode =
        profile.value.residenceCountry ?? platform.settings?.operatingCountry ?? ''
    firstName.value = profile.value.firstName ?? ''
    lastName.value = profile.value.lastName ?? ''
    middleName.value = profile.value.middleName ?? ''
    if (profile.value.address) address.value = { ...profile.value.address }
    channels.value = (
      await api.get<{ channels: string[] }>('/api/v1/profile/verification/channels')
    ).channels
    await restoreContact()
  } catch {
    error.value = 'Please refresh to load your profile.'
  }
})
watch(field, (value) => {
  if (restoringContact) return
  clearVerification('contact', auth.user?.id)
  channel.value = value === 'email' ? 'email' : 'sms'
  destination.value = ''
  challenge.value = null
  code.value = ''
})
async function restoreContact() {
  const saved = readVerification('contact', auth.user?.id)
  if (!saved) return
  restoringContact = true
  field.value = saved.field === 'phone' ? 'phone' : 'email'
  channel.value = saved.channel ?? (field.value === 'phone' ? 'sms' : 'email')
  destination.value = saved.maskedDestination
  await nextTick()
  challenge.value = {
    id: saved.id,
    maskedDestination: saved.maskedDestination,
    expiresAt: saved.expiresAt
  }
  restoringContact = false
  activeTab.value = 'details'
  await nextTick()
  if (contactDisclosure.value) contactDisclosure.value.open = true
}
async function run(action: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  success.value = ''
  try {
    await action()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Please check your connection and try again.'
  } finally {
    busy.value = false
  }
}
async function saveName() {
  await run(async () => {
    const hasAddress = [
      address.value.line1,
      address.value.line2,
      address.value.suburb,
      address.value.city,
      address.value.province,
      address.value.postalCode
    ].some((value) => value?.trim())
    await api.post('/api/v1/profile', {
      firstName: firstName.value,
      lastName: lastName.value,
      middleName: middleName.value,
      address: hasAddress ? address.value : null
    })
    if (auth.user) auth.user.displayName = firstName.value + ' ' + lastName.value
    success.value = 'Your personal details are up to date.'
  })
}
const documentIssuer = ref(''),
  documentType = ref('')
const issuerCountry = computed(() =>
    platform.settings?.countries.find((c) => c.code === documentIssuer.value)
  ),
  documentPolicy = computed(() =>
    issuerCountry.value?.documents.find((d) => d.key === documentType.value)
  ),
  isNationalId = computed(() => documentPolicy.value?.validator === 'south-african-id')
const addressCountry = computed(() =>
  platform.settings?.countries.find((c) => c.code === address.value.countryCode)
)
const identityNumber = ref(''),
  identityPassword = ref(''),
  birthDate = ref(''),
  identityConfirmed = ref(false)

watch(documentIssuer, () => {
  documentType.value =
    issuerCountry.value?.documents.find((d) => d.key === profile.value?.identityDocumentType)
      ?.key ??
    issuerCountry.value?.documents[0]?.key ??
    ''
  identityNumber.value = ''
  birthDate.value = ''
  identityConfirmed.value = false
})
watch(documentType, () => {
  identityNumber.value = ''
  birthDate.value = ''
  identityConfirmed.value = false
})
watch(identityNumber, () => {
  if (isNationalId.value) birthDate.value = ''
  identityConfirmed.value = false
})
async function saveIdentity() {
  if (!identityConfirmed.value || !documentPolicy.value) return
  await run(async () => {
    try {
      await api.post('/api/v1/profile/document', {
        documentType: documentType.value,
        issuerCountry: documentIssuer.value,
        documentNumber: identityNumber.value,
        dateOfBirth: isNationalId.value ? null : birthDate.value || null,
        birthCentury: null,
        currentPassword: identityPassword.value
      })
      identityNumber.value = ''
      profile.value = await api.get<Profile>('/api/v1/profile')
      success.value = 'Your identity details are saved and locked.'
    } finally {
      identityPassword.value = ''
    }
  })
}
onUnmounted(() => {
  identityNumber.value = ''
  identityPassword.value = ''
  password.value = ''
})
async function sendCode() {
  const owner = auth.user?.id
  await run(async () => {
    const result = await api.post<{ id: string; maskedDestination: string; expiresAt: string }>(
      '/api/v1/profile/verification/start',
      {
        field: field.value,
        destination:
          field.value === 'phone'
            ? normalizePhoneNumber(destination.value, phoneCountry.value)
            : destination.value,
        phoneCountry: field.value === 'phone' ? phoneCountry.value : undefined,
        channel: channel.value,
        currentPassword: password.value
      }
    )
    if (auth.user?.id !== owner) return
    challenge.value = result
    if (challenge.value)
      saveVerification(
        'contact',
        {
          id: challenge.value.id,
          maskedDestination: challenge.value.maskedDestination,
          expiresAt: challenge.value.expiresAt,
          field: field.value,
          channel: channel.value
        },
        auth.user?.id
      )
    success.value =
      'Your verification code has been requested. Check your selected contact channel.'
  })
}
async function complete() {
  await run(async () => {
    await api.post('/api/v1/profile/verification/complete', {
      challengeId: challenge.value?.id,
      code: code.value,
      currentPassword: password.value
    })
    clearVerification('contact', auth.user?.id)
    auth.clearSession()
    password.value = ''
    code.value = ''
    challenge.value = null
    await router.replace({ path: '/sign-in', query: { updated: 'contact' } })
  })
}
function resetContactChallenge() {
  clearVerification('contact', auth.user?.id)
  challenge.value = null
  code.value = ''
}
useFeedback(error, success, undefined)
</script>
<template>
  <AppShell>
    <PageHeading title="Profile &amp; Security">
      <SignOutAction />
    </PageHeading>
    <nav
      class="profile-sections w3-light-grey w3-round-large w3-margin-bottom w3-row w3-bar w3-border w3-white w3-padding-small"
      aria-label="Profile Sections"
    >
      <button
        :class="{ 'w3-theme-l4 w3-text-black': (activeTab === 'details') === true }"
        class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
        type="button"
        :aria-pressed="activeTab === 'details'"
        @click="activeTab = 'details'"
      >
        Details
      </button>
      <button
        :class="{ 'w3-theme-l4 w3-text-black': (activeTab === 'security') === true }"
        class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
        type="button"
        :aria-pressed="activeTab === 'security'"
        @click="activeTab = 'security'"
      >
        Security
      </button>
      <button
        :class="{ 'w3-theme-l4 w3-text-black': (activeTab === 'alerts') === true }"
        class="w3-button w3-round-large w3-center w3-col s4 w3-padding-16"
        type="button"
        :aria-pressed="activeTab === 'alerts'"
        @click="activeTab = 'alerts'"
      >
        Alerts
      </button>
    </nav>
    <p
      v-if="error"
      role="alert"
      class="form-alert w3-panel w3-pale-red w3-leftbar w3-border-red"
    >
      {{ error }}
    </p>
    <p
      v-if="success"
      role="status"
      class="success-notice w3-panel w3-pale-green w3-leftbar w3-border-green"
    >
      {{ success }}
    </p>
    <div
      v-if="profile"
      class="profile-grid w3-row"
    >
      <div
        v-if="activeTab === 'details'"
        class="w3-col s12 m12 l6 w3-margin-bottom w3-padding-small"
      >
        <section
          class="surface-panel ops-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        >
          <h2 class="w3-sans-serif w3-large">Personal Details</h2>
          <dl class="w3-section">
            <dt class="w3-small w3-text-grey">Email</dt>
            <dd class="w3-margin-bottom w3-margin-left w3-small">{{ profile.email }}</dd>
            <dt class="w3-small w3-text-grey">Mobile</dt>
            <dd class="w3-margin-bottom w3-margin-left">{{ profile.phone || 'Not added yet' }}</dd>
            <dt class="w3-small w3-text-grey">Profile Registered</dt>
            <dd class="w3-margin-bottom w3-margin-left w3-small">
              {{ formatDate(profile.registeredAt) }}
            </dd>
          </dl>
          <form
            class="ops-form"
            @submit.prevent="saveName"
          >
            <div class="profile-name-fields">
              <div class="w3-col s12 m6 w3-margin-bottom">
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  First Name
                  <input
                    v-model.trim="firstName"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    required
                    maxlength="100"
                    autocomplete="given-name"
                  />
                </label>
              </div>
              <div class="w3-col s12 m6 w3-margin-bottom">
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Surname
                  <input
                    v-model.trim="lastName"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    required
                    maxlength="100"
                    autocomplete="family-name"
                  />
                </label>
              </div>
            </div>
            <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
              Middle Name
              <small class="w3-small">(Optional)</small>
              <input
                v-model.trim="middleName"
                class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                maxlength="100"
                autocomplete="additional-name"
              />
            </label>
            <DisclosurePanel
              class="address-disclosure"
              :open="!!profile.address"
            >
              <template #heading>Residential Address</template>
              <div class="address-fields">
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Street Address
                  <input
                    v-model.trim="address.line1"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    maxlength="200"
                    :required="
                      [
                        address.line1,
                        address.line2,
                        address.suburb,
                        address.city,
                        address.province,
                        address.postalCode
                      ].some((value) => value?.trim())
                    "
                    autocomplete="address-line1"
                  />
                </label>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Apartment Or Unit
                  <small class="w3-small">(Optional)</small>
                  <input
                    v-model.trim="address.line2"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    maxlength="200"
                    autocomplete="address-line2"
                  />
                </label>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Suburb
                  <input
                    v-model.trim="address.suburb"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    maxlength="100"
                  />
                </label>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  City Or Town
                  <input
                    v-model.trim="address.city"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    maxlength="100"
                    :required="
                      [
                        address.line1,
                        address.line2,
                        address.suburb,
                        address.city,
                        address.province,
                        address.postalCode
                      ].some((value) => value?.trim())
                    "
                    autocomplete="address-level2"
                  />
                </label>
                <div class="profile-name-fields w3-row-padding">
                  <div class="w3-col s12 m6 w3-margin-bottom">
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      {{ addressCountry?.regionLabel ?? 'Region' }}
                      <select
                        v-if="addressCountry?.regions.length"
                        v-model="address.province"
                        class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                      >
                        <option value="">Select {{ addressCountry.regionLabel }}</option>
                        <option
                          v-for="region in addressCountry.regions"
                          :key="region.code"
                          :value="region.name"
                        >
                          {{ region.name }}
                        </option>
                      </select>
                      <input
                        v-else
                        v-model.trim="address.province"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        maxlength="100"
                        autocomplete="address-level1"
                      />
                    </label>
                  </div>
                  <div class="w3-col s12 m6 w3-margin-bottom">
                    <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                      Postal Code
                      <input
                        v-model.trim="address.postalCode"
                        class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                        maxlength="20"
                        autocomplete="postal-code"
                      />
                    </label>
                  </div>
                </div>
                <CountryPicker
                  id="profile-country"
                  v-model="address.countryCode"
                  :countries="platform.settings?.countries.filter((c) => c.active) ?? []"
                  label="Country Of Residence"
                />
              </div>
            </DisclosurePanel>
            <button
              class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
              :disabled="busy"
            >
              Save Personal Details
            </button>
          </form>
        </section>
      </div>
      <div
        v-if="activeTab === 'details'"
        class="w3-col s12 m12 l6 w3-margin-bottom w3-padding-small"
      >
        <section
          class="surface-panel ops-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        >
          <DisclosurePanel class="profile-contact-disclosure">
            <template #heading>Identity Details</template>
            <div
              v-if="profile.maskedIdentityNumber"
              class="w3-responsive"
            >
              <table class="account-details w3-table w3-bordered">
                <tbody>
                  <tr>
                    <th
                      class="w3-mobile w3-text-grey w3-small"
                      scope="row"
                    >
                      {{
                        issuerCountry?.documents.find(
                          (d) => d.key === profile?.identityDocumentType
                        )?.label ?? 'Identity Document'
                      }}
                    </th>
                    <td class="w3-mobile">
                      <IdentityNumberControl
                        :owner="profile.id"
                        :masked="profile.maskedIdentityNumber"
                      />
                    </td>
                  </tr>
                  <tr>
                    <th
                      class="w3-mobile w3-text-grey w3-small"
                      scope="row"
                    >
                      Date Of Birth
                    </th>
                    <td class="w3-mobile">
                      {{
                        profile.dateOfBirth ? formatBirthDate(profile.dateOfBirth) : 'Not recorded'
                      }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <form
              v-else
              class="ops-form identity-form"
              @submit.prevent="saveIdentity"
            >
              <CountryPicker
                id="profile-document-issuer"
                v-model="documentIssuer"
                :countries="platform.settings?.countries.filter((c) => c.active) ?? []"
                label="Issuing Country"
              />
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Document Type
                <select
                  v-model="documentType"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  required
                >
                  <option
                    v-for="doc in issuerCountry?.documents"
                    :key="doc.key"
                    :value="doc.key"
                  >
                    {{ doc.label }}
                  </option>
                </select>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                {{ documentPolicy?.label ?? 'Document Number' }}
                <input
                  v-model.trim="identityNumber"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  name="identity-document-number"
                  :inputmode="isNationalId ? 'numeric' : 'text'"
                  :pattern="documentPolicy?.pattern"
                  maxlength="100"
                  autocomplete="off"
                  autocapitalize="characters"
                  spellcheck="false"
                  required
                  aria-describedby="identity-lock-note"
                />
              </label>
              <label
                v-if="!isNationalId"
                class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
              >
                Date Of Birth
                <input
                  v-model="birthDate"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  type="date"
                />
              </label>
              <p
                v-else
                class="field-hint w3-small w3-text-grey w3-block"
              >
                Your date of birth is validated from your identity document when saved.
              </p>
              <p
                id="identity-lock-note"
                class="field-hint w3-small w3-text-grey w3-block"
              >
                Once saved, your document details are locked. The requirements follow the selected
                issuing country.
              </p>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Current Password
                <PasswordInput
                  v-model="identityPassword"
                  autocomplete="current-password"
                  required
                  :maxlength="reference.password?.maximum ?? undefined"
                />
              </label>
              <label class="identity-confirm w3-block w3-margin-top w3-text-dark-grey w3-medium">
                <input
                  v-model="identityConfirmed"
                  class="w3-check"
                  type="checkbox"
                  required
                />
                I Confirm These Identity Details Are Correct.
              </label>
              <button
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="busy || !identityConfirmed || !documentPolicy"
              >
                Save Identity Details
              </button>
            </form>
          </DisclosurePanel>
        </section>
      </div>
      <div
        v-if="activeTab === 'details'"
        class="w3-col s12 m12 l6 w3-margin-bottom w3-padding-small"
      >
        <section
          class="surface-panel ops-panel w3-container w3-margin-bottom w3-section w3-white w3-padding-large w3-round-xlarge w3-border w3-border-light-grey"
        >
          <DisclosurePanel
            ref="contactDisclosure"
            class="profile-contact-disclosure"
          >
            <template #heading>Change Email Or Mobile</template>
            <p
              v-if="!channels.length"
              class="info-notice w3-panel w3-pale-blue w3-leftbar w3-border-blue"
            >
              Contact verification will be available soon.
            </p>
            <form
              class="ops-form"
              @submit.prevent="challenge ? complete() : sendCode()"
            >
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Contact To Update
                <select
                  v-model="field"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  :disabled="busy || !!challenge"
                >
                  <option value="email">Email Address</option>
                  <option value="phone">Mobile Number</option>
                </select>
              </label>
              <CountryPicker
                v-if="field === 'phone'"
                id="contact-phone-country"
                v-model="phoneCountry"
                :countries="phoneCountries"
                label="Country Calling Code"
                dial
                :disabled="busy || !!challenge"
              />
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                New {{ field === 'email' ? 'Email Address' : 'Mobile Number' }}
                <input
                  v-model.trim="destination"
                  class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  :type="field === 'email' ? 'email' : 'tel'"
                  :placeholder="field === 'email' ? 'you@example.com' : 'Mobile number'"
                  :disabled="!!challenge"
                  required
                  maxlength="320"
                />
              </label>
              <label
                v-if="field === 'phone'"
                class="w3-block w3-margin-top w3-text-dark-grey w3-medium"
              >
                Send Verification By
                <select
                  v-model="channel"
                  class="w3-select w3-border w3-round-xlarge w3-large w3-border-light-grey"
                  :disabled="!!challenge"
                >
                  <option value="sms">SMS</option>
                  <option value="whatsapp">WhatsApp</option>
                </select>
              </label>
              <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                Current Password
                <PasswordInput
                  v-model="password"
                  autocomplete="current-password"
                  required
                  :maxlength="reference.password?.maximum ?? undefined"
                />
              </label>
              <template v-if="challenge">
                <p>
                  Code requested for
                  {{ challenge.maskedDestination }}. Valid until
                  {{ formatDate(challenge.expiresAt, { hour: '2-digit', minute: '2-digit' }) }}.
                </p>
                <label class="w3-block w3-margin-top w3-text-dark-grey w3-medium">
                  Six-Digit Verification Code
                  <input
                    v-model.trim="code"
                    class="w3-input w3-border w3-round-xlarge w3-large w3-border-light-grey"
                    inputmode="numeric"
                    pattern="[0-9]{6}"
                    maxlength="6"
                    autocomplete="one-time-code"
                    required
                  />
                </label>
              </template>
              <button
                class="button button-primary w3-button w3-theme-d5 w3-block w3-round-large w3-padding-16 w3-margin-bottom w3-margin-top"
                :disabled="busy || !channels.includes(channel)"
              >
                {{
                  busy
                    ? 'Please Wait…'
                    : challenge
                      ? 'Verify & Save Contact'
                      : 'Send Verification Code'
                }}
              </button>
              <button
                v-if="challenge"
                type="button"
                class="button button-secondary w3-button w3-border w3-block w3-round-large w3-padding w3-margin-bottom"
                :disabled="busy"
                @click="resetContactChallenge"
              >
                Request A New Code
              </button>
            </form>
          </DisclosurePanel>
        </section>
      </div>
    </div>
    <NotificationPreferences v-if="activeTab === 'alerts'" />
    <PasswordChangePanel v-if="activeTab === 'security'" />
    <RememberedDevices v-if="activeTab === 'security'" />
    <SecurityActivity v-if="activeTab === 'security'" />
    <AccountDeletionPanel
      v-if="activeTab === 'security' && auth.user?.roles.includes('customer')"
    />
  </AppShell>
</template>
