import { onMounted, onUnmounted, ref, watch } from 'vue'

export type ThemePreference = 'light' | 'dark' | 'system'
const preference = ref<ThemePreference>('system')
const isDark = ref(false)

export function useTheme() {
  let media: MediaQueryList | undefined
  const apply = () => {
    isDark.value =
      preference.value === 'dark' || (preference.value === 'system' && Boolean(media?.matches))
    document.body.classList.remove('w3-dark-grey', 'w3-light-grey')
    document.body.classList.toggle('w3-black', isDark.value)
    document.body.classList.toggle('w3-theme-l5', !isDark.value)
    document.body.classList.remove('w3-white')
    const scheme = document.querySelector<HTMLMetaElement>('meta[name="color-scheme"]')
    if (scheme) scheme.content = isDark.value ? 'dark' : 'light'
    document.documentElement.dataset.theme = isDark.value ? 'dark' : 'light'
  }
  watch(preference, (value) => {
    try {
      localStorage.setItem('portal-theme', value)
    } catch {
      /* Theme still works when storage is unavailable. */
    }
    apply()
  })
  onMounted(() => {
    media = window.matchMedia('(prefers-color-scheme: dark)')
    try {
      const saved = localStorage.getItem('portal-theme')
      preference.value = saved === 'light' || saved === 'dark' ? saved : 'system'
    } catch {
      preference.value = 'system'
    }
    apply()
    media.addEventListener('change', apply)
  })
  onUnmounted(() => media?.removeEventListener('change', apply))
  return { preference, isDark }
}
