import type { ActionActor } from './types'
export interface AccountBalance {
  accountId: string
  currency: string
  currentBalance: number | null
  availableBalance: number | null
  reservedAmount: number | null
  balanceKnown: boolean
}
export interface BalanceEntry {
  entryId: number
  transactionId: string
  currentDelta: number
  reservedDelta: number
  recordedAt: string
  description: string
}
export interface Refund {
  customerReference?: string | null
  creditReference?: string | null
  disputeId: string
  creditTransactionId: string
  amount: number
  currency: string
  postedAt: string | null
  status?: string
  automatic: boolean
  replayed: boolean
}
export interface RefundRecall {
  id: string
  disputeId: string
  creditTransactionId: string
  reversalTransactionId: string
  amount: number
  currency: string
  postedAt: string | null
  status?: string
  reason: string
  replayed: boolean
}
export interface FinancialSummary {
  canApproveRefund?: boolean
  pending?: {
    transactionId: string
    kind: string
    status: string
    failureCode?: string | null
  } | null
  refund: Refund | null
  decision: string | null
  ruleCode: string | null
  ruleVersion: number | null
  recall?: RefundRecall | null
  canReinstate?: boolean
  canRecall?: boolean
  reinstatedDisputeId?: string | null
  parentDisputeId?: string | null
}
export interface CaseActivity {
  actorIdentity?: ActionActor | null
  id: string
  action: string
  actor: string
  occurredAt: string
  detail: string | null
}
export interface BalanceOverview {
  currency?: string | null
  accounts: AccountBalance[]
  currentBalance: number | null
  availableBalance: number | null
  accountCount: number
  balanceKnown: boolean
}
export function uniqueBalances(items: AccountBalance[]): AccountBalance[] {
  return [...new Map(items.map((item) => [item.accountId, item])).values()]
}
