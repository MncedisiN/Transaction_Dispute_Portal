import type { Settings } from '../../src/platform'

export function platformSettings(operatingCountry = 'ZA'): Settings {
  return {
    version: 1,
    operatingCountry,
    branding: { name: 'Dispute Portal', accentColor: '#00567a', logoColor: '#e7352b' },
    registration: {
      requireEmail: true,
      requirePhone: true,
      requireDocument: true,
      requireAddress: false,
      requireVerification: true
    },
    currencies: [
      { code: 'ZAR', name: 'South African Rand', symbol: 'R', decimals: 2 },
      { code: 'USD', name: 'US Dollar', symbol: '$', decimals: 2 }
    ],
    countries: [
      {
        code: 'ZA',
        name: 'South Africa',
        flag: 'ZA',
        dialCode: '+27',
        locale: 'en-ZA',
        timeZone: 'Africa/Johannesburg',
        defaultCurrency: 'ZAR',
        currencies: ['ZAR'],
        documents: [
          {
            key: 'Passport',
            label: 'Passport',
            pattern: '^[A-Z0-9]{6,20}$',
            validator: 'pattern',
            required: true
          }
        ],
        regions: [],
        regionLabel: 'Region',
        postalLabel: 'Postal Code',
        postalRequired: false,
        active: true
      },
      {
        code: 'US',
        name: 'United States',
        flag: 'US',
        dialCode: '+1',
        locale: 'en-US',
        timeZone: 'America/New_York',
        defaultCurrency: 'USD',
        currencies: ['USD'],
        documents: [
          {
            key: 'Passport',
            label: 'Passport',
            pattern: '^[A-Z0-9]{6,20}$',
            validator: 'pattern',
            required: true
          }
        ],
        regions: [],
        regionLabel: 'State',
        postalLabel: 'ZIP Code',
        postalRequired: false,
        active: true
      }
    ],
    financialRules: [
      {
        country: 'ZA',
        currency: 'ZAR',
        minimum: 1,
        maximum: 10000,
        dailyLimit: 20000,
        fixedFee: 0,
        percentageFee: 0
      }
    ],
    accountTypes: ['Cheque', 'Savings'],
    maximumAccounts: 10,
    accountNumberDigits: 10,
    reservationSeconds: 60
  }
}
