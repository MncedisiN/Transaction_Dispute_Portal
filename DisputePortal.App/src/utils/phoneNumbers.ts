import {
  getCountries,
  getCountryCallingCode,
  parsePhoneNumberFromString,
  type CountryCode
} from 'libphonenumber-js/max'

export function phoneCountries(locale: string) {
  const names = new Intl.DisplayNames([locale], { type: 'region' })
  return getCountries()
    .map((code) => ({
      code,
      name: names.of(code) ?? code,
      dialCode: '+' + getCountryCallingCode(code)
    }))
    .sort((left, right) => left.name.localeCompare(right.name))
}

export function normalizePhoneNumber(value: string, country: string): string {
  if (value.length > 40 || !/^[+0-9 ()-]+$/.test(value))
    throw new Error('Check your mobile number and its selected country code.')
  const number = parsePhoneNumberFromString(value, country as CountryCode)
  if (!number?.isValid() || number.country !== country)
    throw new Error('Check your mobile number and its selected country code.')
  return number.number
}
