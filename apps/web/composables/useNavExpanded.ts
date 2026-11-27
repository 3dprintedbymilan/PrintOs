const storageKey = 'printos_nav'

function readStored(): boolean | null {
  try {
    const value = localStorage.getItem(storageKey)
    return value === 'expanded' ? true : value === 'collapsed' ? false : null
  }
  catch {
    return null
  }
}

export function useNavExpanded() {
  const stored = useState<boolean | null>('nav-stored', () => null)
  const wide = useState('nav-wide', () => false)
  const initialised = useState('nav-init', () => false)

  if (import.meta.client && !initialised.value) {
    initialised.value = true
    stored.value = readStored()
    const query = window.matchMedia('(min-width: 1200px)')
    wide.value = query.matches
    query.addEventListener('change', (event) => {
      wide.value = event.matches
    })
  }

  const expanded = computed(() => stored.value ?? wide.value)

  function set(value: boolean) {
    stored.value = value
    try {
      localStorage.setItem(storageKey, value ? 'expanded' : 'collapsed')
    }
    catch {
      // storage unavailable: the choice only lasts for this visit
    }
  }

  return { expanded, wide, set }
}
