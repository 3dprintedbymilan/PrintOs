import { notificationPollMilliseconds } from '~/utils/notifications'

let timer: ReturnType<typeof setInterval> | undefined
let connection: { stop: () => Promise<void> } | undefined
let started = false

export function useNotifications() {
  const auth = useAuthStore()
  const unread = useState<number>('notifications-unread', () => 0)
  const changeTick = useState<number>('notifications-change', () => 0)
  const realtime = useState<boolean>('notifications-realtime', () => false)

  async function refresh() {
    if (!auth.isSignedIn) return
    try {
      const result = await auth.authorizedFetch<{ count: number }>('/api/notifications/unread-count')
      if (result.count !== unread.value) changeTick.value += 1
      unread.value = result.count
    }
    catch {
      // the badge keeps its last value; the next poll tries again
    }
  }

  async function connect() {
    try {
      const signalr = await import('@microsoft/signalr')
      const hub = new signalr.HubConnectionBuilder()
        .withUrl(`${useRuntimeConfig().public.apiBase}/hubs/notifications`, {
          accessTokenFactory: () => auth.session?.accessToken ?? '',
          withCredentials: false,
        })
        .withAutomaticReconnect()
        .configureLogging(signalr.LogLevel.None)
        .build()
      hub.on('notificationsChanged', () => {
        changeTick.value += 1
        void refresh()
      })
      hub.onclose(() => { realtime.value = false })
      hub.onreconnected(() => {
        realtime.value = true
        void refresh()
      })
      await hub.start()
      connection = hub
      realtime.value = true
    }
    catch {
      realtime.value = false
    }
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') void refresh()
  }

  function start() {
    if (started || !auth.isSignedIn) return
    started = true
    void refresh()
    timer = setInterval(() => {
      if (document.visibilityState === 'visible') void refresh()
    }, notificationPollMilliseconds)
    document.addEventListener('visibilitychange', onVisibilityChange)
    void connect()
  }

  function stop() {
    started = false
    if (timer) clearInterval(timer)
    timer = undefined
    document.removeEventListener('visibilitychange', onVisibilityChange)
    void connection?.stop()
    connection = undefined
    realtime.value = false
  }

  return { unread, changeTick, realtime, refresh, start, stop }
}
