import type { BambuStatus } from '~/utils/bambu'
import { pollIntervalMilliseconds } from '~/utils/bambu'

export function useBambuStatus() {
  const auth = useAuthStore()
  const status = ref<BambuStatus | null>(null)
  const state = ref<'loading' | 'ready' | 'failed'>('loading')
  let timer: ReturnType<typeof setInterval> | undefined

  async function load() {
    try {
      status.value = await auth.authorizedFetch<BambuStatus>('/api/bambu/status')
      state.value = 'ready'
    }
    catch {
      if (!status.value) state.value = 'failed'
    }
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') load()
  }

  function startPolling() {
    stopPolling()
    timer = setInterval(() => {
      if (document.visibilityState === 'visible') load()
    }, pollIntervalMilliseconds)
  }

  function stopPolling() {
    if (timer) clearInterval(timer)
    timer = undefined
  }

  async function reload() {
    state.value = status.value ? 'ready' : 'loading'
    await load()
  }

  onMounted(() => {
    load()
    startPolling()
    document.addEventListener('visibilitychange', onVisibilityChange)
  })

  onBeforeUnmount(() => {
    stopPolling()
    document.removeEventListener('visibilitychange', onVisibilityChange)
  })

  return { status, state, reload }
}
