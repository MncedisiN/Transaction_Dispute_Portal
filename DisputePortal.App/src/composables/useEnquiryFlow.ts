import { computed, ref, type Ref } from 'vue'
import { reference } from '../portalReference'
import type { EnquiryDraft } from '../utils/enquiryDraft'
import type { SupportConcern } from '../operations'

export function useEnquiryFlow(
  draft: EnquiryDraft,
  concern: Readonly<Ref<SupportConcern | undefined>>,
  relatedCase: boolean
) {
  const flow = computed(() => reference.enquiryFlow)
  const stepCode = ref(relatedCase ? flow.value?.relatedCaseInitialStep : flow.value?.initialStep)
  const current = computed(() => flow.value?.steps.find((step) => step.code === stepCode.value))
  const step = computed(() => current.value?.kind ?? '')
  const phase = computed(() => current.value?.phase ?? '')
  const prompt = computed(() => current.value?.prompt ?? '')

  function goTo(kind: string) {
    const target = flow.value?.steps.find((step) => step.kind === kind)
    if (!target) throw new Error('This enquiry control is unavailable. Please reload the portal.')
    stepCode.value = target.code
  }

  function nextDetails() {
    const next = flow.value?.steps.find((candidate) => {
      if (!candidate.completionField || !(candidate.completionField in draft)) return false
      if (candidate.concernFlag && !concern.value?.[candidate.concernFlag as keyof SupportConcern])
        return false
      const value = draft[candidate.completionField as keyof EnquiryDraft]
      return typeof value === 'string' ? !value.trim() : !value
    })
    stepCode.value = next?.code ?? flow.value?.reviewStep
  }

  return { stepCode, step, phase, prompt, goTo, nextDetails }
}
