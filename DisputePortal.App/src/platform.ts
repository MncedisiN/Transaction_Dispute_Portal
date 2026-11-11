import { configureFormatting } from './utils/localeContext'
import { formatMoney, formatDate } from './utils/format'
import { reactive, watch } from 'vue'
import { api } from './api'
export interface Currency {
  code: string
  name: string
  symbol: string
  decimals: number
}
export interface Country {
  code: string
  name: string
  flag: string
  dialCode: string
  locale: string
  timeZone: string
  defaultCurrency: string
  currencies: string[]
  documents: { key: string; label: string; pattern: string; validator: string; required: boolean }[]
  regions: { code: string; name: string; districts: string[] }[]
  regionLabel: string
  postalLabel: string
  postalRequired: boolean
  active: boolean
}
export interface Settings {
  publication?: import('./policyVersions').PolicyVersion
  version: number
  operatingCountry: string
  branding: { name: string; accentColor: string; logoColor: string }
  registration: {
    requireEmail: boolean
    requirePhone: boolean
    requireDocument: boolean
    requireAddress: boolean
    requireVerification: boolean
  }
  currencies: Currency[]
  countries: Country[]
  financialRules: {
    country: string
    currency: string
    minimum: number
    maximum: number
    dailyLimit: number
    fixedFee: number
    percentageFee: number
    minimumExact?: string
    maximumExact?: string
    fixedFeeExact?: string
    percentageFeeExact?: string
  }[]
  accountTypes: string[]
  maximumAccounts: number
  accountNumberDigits: number
  reservationSeconds: number
  quoteSeconds?: number
}
export interface FinancialAccount {
  id: string
  userId: string
  number: string
  type: string
  countryCode: string
  currency: string
  status: string
  ledgerBalance: number
  reservedBalance: number
  availableBalance: number
  version: number
  availableBalanceExact?: string
  ledgerBalanceExact?: string
  reservedBalanceExact?: string
  createdAt: string
}
export interface FinancialTransaction {
  typeLabel?: string
  merchantName?: string | null
  direction?: string
  description?: string | null
  id: string
  paymentId: string
  kind: string
  reference: string
  accountId: string
  recipientAccountId?: string
  amount: number
  currency: string
  fee: number
  status: string
  failureCode?: string
  createdAt: string
}
export interface Quote {
  id: string
  kind: string
  accountId: string
  recipientAccountId?: string
  amount: number
  currency: string
  fee: number
  expiresAt: string
  amountExact?: string
  feeExact?: string
  totalDebitExact?: string
}
export const platform = reactive({
  settings: null as Settings | null,
  enabled: null as boolean | null
})
let pending: Promise<Settings> | null = null
export async function loadPlatform(refresh = false) {
  if (platform.settings && !refresh) return platform.settings
  if (!pending)
    pending = api
      .get<Settings>('/api/v1/platform/settings')
      .then((settings) => {
        platform.settings = settings
        platform.enabled = true
        return settings
      })
      .finally(() => {
        pending = null
      })
  return pending
}
watch(
  () => platform.settings,
  (value) => {
    if (value) {
      configureFormatting(value)
    }
  },
  { flush: 'sync' }
)
export function money(amount: number, currency: string, countryCode?: string) {
  return formatMoney(amount, currency, countryCode)
}
export function localDate(value: string, countryCode?: string) {
  return formatDate(value, { dateStyle: 'medium', timeStyle: 'short' }, countryCode)
}

export function operatingCountry() {
  return platform.settings?.countries.find(
    (country) => country.code === platform.settings?.operatingCountry
  )
}
