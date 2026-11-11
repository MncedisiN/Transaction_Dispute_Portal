import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '../api'
import type { Account } from '../types'

function defaultActivity() {
  return { search: '', direction: '', type: '', sort: 'date_desc', from: '', to: '', page: 1 }
}

export const useAccountsStore = defineStore('accounts', () => {
  const items = ref<Account[]>([])
  const selectedId = ref('')
  const owner = ref('')
  const loaded = ref(false)
  const activity = ref(defaultActivity())
  const selected = computed(() => items.value.find((account) => account.id === selectedId.value))
  let pending: Promise<void> | null = null
  let generation = 0

  function reset() {
    generation++
    pending = null
    items.value = []
    selectedId.value = ''
    owner.value = ''
    loaded.value = false
    activity.value = defaultActivity()
  }

  async function load(userId: string) {
    if (owner.value === userId && loaded.value) return
    if (owner.value === userId && pending) return pending
    reset()
    owner.value = userId
    const current = generation
    pending = api
      .get<Account[]>('/api/v1/accounts')
      .then((accounts) => {
        if (current === generation) {
          items.value = accounts
          loaded.value = true
        }
      })
      .finally(() => {
        if (current === generation) pending = null
      })
    return pending
  }

  async function refresh(userId: string) {
    if (owner.value !== userId) return load(userId)
    const current = generation
    const accounts = await api.get<Account[]>('/api/v1/accounts')
    if (current !== generation || owner.value !== userId) return
    items.value = accounts
    loaded.value = true
    if (selectedId.value && !accounts.some((account) => account.id === selectedId.value))
      selectedId.value = ''
  }

  return { items, selectedId, owner, loaded, activity, selected, load, refresh, reset }
})
