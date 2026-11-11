import type { ResidentialAddress } from './operations'
export interface StaffTransaction {
  refundReference?: string | null
  id: string
  customerId: string
  customerName: string
  email: string | null
  accountId: string
  accountType: string
  accountNumber: string
  reference: string
  transactionAt: string
  description: string
  merchantName: string
  amount: number
  currency: string
  type: string
  direction: 'Debit' | 'Credit'
  status: string
  disputeEligible: boolean
  disputeId: string | null
}
export interface StaffTransactionDetail {
  transaction: StaffTransaction
  customer: {
    id: string
    displayName: string
    firstName: string | null
    middleName: string | null
    lastName: string | null
    email: string | null
    phone: string | null
    identityNumber: string | null
    dateOfBirth: string | null
    address: ResidentialAddress | null
    status: string
    registeredAt: string
  }
  fullAccountNumber: string | null
  accountStatus: string
}
