import { isThemePreference, resolveTheme, themeStorageKey, type ThemePreference } from '~/utils/theme'

function readStoredPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(themeStorageKey)
    return isThemePreference(stored) ? stored : 'system'
  }
  catch {
    return 'system'
  }
}

export function useTheme() {
  const preference = useState<ThemePreference>('theme-preference', () => 'system')

  function apply() {
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
    document.documentElement.dataset.theme = resolveTheme(preference.value, prefersDark)
  }

  function setPreference(value: ThemePreference) {
    preference.value = value
    try {
      localStorage.setItem(themeStorageKey, value)
    }
    catch {
      // storage unavailable (private mode); the choice then lasts for this session only
    }
    apply()
  }

  function init() {
    preference.value = readStoredPreference()
    apply()
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', apply)
  }

  return { preference, setPreference, init }
}
