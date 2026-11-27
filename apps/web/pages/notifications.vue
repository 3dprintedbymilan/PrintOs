<script setup lang="ts">
import { severityTone, textKey } from '~/utils/notifications'
import type { AppNotification, NotificationList } from '~/utils/notifications'

definePageMeta({ titleKey: 'notifications.title' })

const { t, d } = useI18n()
const auth = useAuthStore()
const { unread, changeTick, refresh } = useNotifications()

const filter = ref('all')
const items = ref<AppNotification[]>([])
const hasMore = ref(false)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const loadingMore = ref(false)
const busy = ref(false)
const message = ref('')
const failure = ref('')

const filterOptions = computed(() => [
  { value: 'all', label: t('notifications.filters.all') },
  { value: 'unread', label: t('notifications.filters.unread') },
])

async function load(silent = false) {
  if (!silent) status.value = 'loading'
  try {
    const result = await auth.authorizedFetch<NotificationList>('/api/notifications', { query: { unreadOnly: filter.value === 'unread', limit: 30 } })
    items.value = result.items
    hasMore.value = result.hasMore
    unread.value = result.unreadCount
    status.value = 'ready'
  }
  catch {
    if (!silent) status.value = 'failed'
  }
}

async function loadMore() {
  const last = items.value[items.value.length - 1]
  if (!last) return
  loadingMore.value = true
  try {
    const result = await auth.authorizedFetch<NotificationList>('/api/notifications', { query: { unreadOnly: filter.value === 'unread', limit: 30, before: last.createdAt } })
    items.value = [...items.value, ...result.items]
    hasMore.value = result.hasMore
  }
  catch {
    failure.value = t('notifications.errors.action')
  }
  finally {
    loadingMore.value = false
  }
}

async function act(task: () => Promise<void>, done: string) {
  busy.value = true
  failure.value = ''
  message.value = ''
  try {
    await task()
    message.value = done
    await load(true)
    await refresh()
  }
  catch {
    failure.value = t('notifications.errors.action')
  }
  finally {
    busy.value = false
  }
}

const markRead = (item: AppNotification) => act(async () => {
  await auth.authorizedFetch(`/api/notifications/${item.id}/read`, { method: 'POST' })
}, t('notifications.markedRead'))

const remove = (item: AppNotification) => act(async () => {
  await auth.authorizedFetch(`/api/notifications/${item.id}`, { method: 'DELETE' })
}, t('notifications.removed'))

const markAllRead = () => act(async () => {
  await auth.authorizedFetch('/api/notifications/read-all', { method: 'POST' })
}, t('notifications.allRead'))

async function open(item: AppNotification) {
  if (!item.isRead) {
    try {
      await auth.authorizedFetch(`/api/notifications/${item.id}/read`, { method: 'POST' })
      void refresh()
    }
    catch {
      // opening the target still works
    }
  }
  if (item.linkPath) await navigateTo(item.linkPath)
}

function textOf(item: AppNotification) {
  const parameters = { ...item.parameters }
  if (parameters.date) parameters.date = d(new Date(`${parameters.date}T00:00:00`), { dateStyle: 'medium' })
  return t(textKey(item), parameters)
}

const when = (value: string) => d(new Date(value), { dateStyle: 'medium', timeStyle: 'short' })

watch(filter, () => load())
watch(changeTick, () => load(true))
onMounted(() => load())
</script>

<template>
  <div class="centre">
    <div class="bar">
      <NuxtLink
        to="/notification-settings"
        class="link"
      >
        {{ $t('notifications.settingsLink') }}
      </NuxtLink>
      <UiButton
        variant="secondary"
        :disabled="unread === 0 || busy"
        @click="markAllRead"
      >
        {{ $t('notifications.markAllRead') }}
      </UiButton>
    </div>

    <UiSegmentedControl
      v-model="filter"
      :label="$t('notifications.filterLabel')"
      :options="filterOptions"
    />

    <p
      class="notice"
      role="status"
    >
      {{ message }}
    </p>
    <p
      v-if="failure"
      class="error"
      role="alert"
    >
      {{ failure }}
    </p>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load()"
    />
    <template v-else>
      <StateEmpty
        v-if="items.length === 0"
        :message="filter === 'unread' ? $t('notifications.emptyUnread') : $t('notifications.empty')"
      />
      <ul
        v-else
        class="list"
      >
        <li
          v-for="item in items"
          :key="item.id"
        >
          <UiCard
            class="item"
            :class="{ unread: !item.isRead }"
          >
            <div class="head">
              <UiStatusChip :status="severityTone(item.severity)">
                {{ $t(`notifications.types.${item.type}.label`) }}
              </UiStatusChip>
              <time
                class="when"
                :datetime="item.createdAt"
              >{{ when(item.createdAt) }}</time>
            </div>
            <p class="text">
              <span
                v-if="!item.isRead"
                class="sr-only"
              >{{ $t('notifications.unreadMark') }}</span>
              {{ textOf(item) }}
            </p>
            <div class="actions">
              <UiButton
                v-if="item.linkPath"
                variant="secondary"
                @click="open(item)"
              >
                {{ $t('notifications.open') }}
              </UiButton>
              <UiButton
                v-if="!item.isRead"
                variant="ghost"
                :disabled="busy"
                @click="markRead(item)"
              >
                {{ $t('notifications.markRead') }}
              </UiButton>
              <UiButton
                variant="ghost"
                :disabled="busy"
                @click="remove(item)"
              >
                {{ $t('notifications.remove') }}
              </UiButton>
            </div>
          </UiCard>
        </li>
      </ul>
      <UiButton
        v-if="hasMore"
        variant="secondary"
        block
        :loading="loadingMore"
        @click="loadMore"
      >
        {{ $t('notifications.loadMore') }}
      </UiButton>
    </template>
  </div>
</template>

<style scoped>
.centre { display: grid; gap: var(--space-3); }
.bar { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); flex-wrap: wrap; }
.link { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; }
.list { display: grid; gap: var(--space-3); list-style: none; margin: 0; padding: 0; }
.item { display: grid; gap: var(--space-2); }
.item.unread { border-left: 3px solid var(--accent); }
.head { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); flex-wrap: wrap; }
.when { color: var(--muted); font-size: var(--font-size-sm); }
.text { margin: 0; overflow-wrap: anywhere; }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.notice { margin: 0; min-height: 1.2em; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
