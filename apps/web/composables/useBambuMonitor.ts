import type { BambuMonitor } from '~/utils/bambuMonitor'
import { pollIntervalMilliseconds } from '~/utils/bambu'

export function useBambuMonitor() {
  const auth = useAuthStore()
  const monitor = ref<BambuMonitor | null>(null)
  const state = ref<'loading' | 'ready' | 'failed'>('loading')
  const refreshFailed = ref(false)
  const now = ref(Date.now())
  let timer: ReturnType<typeof setInterval> | undefined

  async function load() {
    try {
      monitor.value = await auth.authorizedFetch<BambuMonitor>('/api/bambu/monitor')
      state.value = 'ready'
      refreshFailed.value = false
    }
    catch {
      if (monitor.value) refreshFailed.value = true
      else state.value = 'failed'
    }
    now.value = Date.now()
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') load()
  }

  async function reload() {
    state.value = monitor.value ? 'ready' : 'loading'
    await load()
  }

  onMounted(() => {
    load()
    timer = setInterval(() => {
      now.value = Date.now()
      if (document.visibilityState === 'visible') load()
    }, pollIntervalMilliseconds)
    document.addEventListener('visibilitychange', onVisibilityChange)
  })

  onBeforeUnmount(() => {
    if (timer) clearInterval(timer)
    document.removeEventListener('visibilitychange', onVisibilityChange)
  })

  return { monitor, state, refreshFailed, now, reload }
}
