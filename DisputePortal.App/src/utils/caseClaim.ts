import type { CaseOperations, ClaimAction } from '../operations'
export type ClaimEligibility = ClaimAction
// The server supplies the actor-specific decision. Missing capabilities stay unavailable.
export function caseClaimEligibility(item: CaseOperations): ClaimEligibility {
  return (
    item.actions?.claim ?? {
      allowed: false,
      state: 'locked',
      label: 'Case Locked',
      reason: 'Refresh to check case access.'
    }
  )
}
