export interface SubmissionPolicy {
  version: number
  highValueThreshold: number
  extraCheckAfterDays: number
  debitOrderWindowDays: number
  debitOrderAppLimit: number
}
export interface AutomaticPolicy {
  version: number
  enabled: boolean
  maximumAmount: number
  duplicateWindowMinutes: number
  maximumAgeHours: number
  dailyLimit: number
}
export interface PolicyLimits {
  maximumMoney: number
  maximumDays: number
  maximumHours: number
}
export interface PolicyConfiguration {
  submission: SubmissionPolicy
  automatic: AutomaticPolicy
}
export interface PolicyDefaults extends PolicyConfiguration {
  limits: PolicyLimits
}
