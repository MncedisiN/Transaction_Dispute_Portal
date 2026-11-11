import { shallowRef } from 'vue'
interface LocaleConfiguration {
  operatingCountry: string
  countries: { code: string; locale: string; timeZone: string; defaultCurrency: string }[]
  currencies: { code: string; decimals: number }[]
}
const configuration = shallowRef<LocaleConfiguration | null>(null)
export function configureFormatting(value: LocaleConfiguration | null) {
  configuration.value = value
}
export function formattingContext(currency?: string, countryCode?: string) {
  const country = configuration.value?.countries.find(
    (c) => c.code === (countryCode ?? configuration.value?.operatingCountry)
  )
  const code = currency || country?.defaultCurrency
  return {
    locale: country?.locale,
    timeZone: country?.timeZone ?? 'UTC',
    currency: code,
    decimals: configuration.value?.currencies.find((c) => c.code === code)?.decimals
  }
}
