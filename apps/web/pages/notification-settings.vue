<script setup lang="ts">
import { parseApiError } from '~/utils/apiError'
import { currentUtcOffsetMinutes, isValidTime } from '~/utils/notifications'
import type { NotificationPreferences, NotificationTypePreference } from '~/utils/notifications'

definePageMeta({ titleKey: 'notificationSettings.title' })

const { t } = useI18n()
const auth = useAuthStore()
const { realtime } = useNotifications()

const preferences = ref<NotificationTypePreference[]>([])
const quietEnabled = ref(false)
const quietStart = ref('22:00')
const quietEnd = ref('07:00')
const emailAvailable = ref(false)
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const saving = ref(false)
const message = ref('')
const failure = ref('')
const errors = ref<{ start?: string, end?: string }>({})

function apply(result: NotificationPreferences) {
  preferences.value = result.types
  quietEnabled.value = result.quietHours.enabled
  quietStart.value = result.quietHours.start
  quietEnd.value = result.quietHours.end
  emailAvailable.value = result.channels.emailAvailable
}

async function load() {
  status.value = 'loading'
  try {
    apply(await auth.authorizedFetch<NotificationPreferences>('/api/notifications/preferences'))
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function save() {
  message.value = ''
  failure.value = ''
  errors.value = {}
  if (quietEnabled.value) {
    if (!isValidTime(quietStart.value)) errors.value.start = t('notificationSettings.errors.time')
    if (!isValidTime(quietEnd.value)) errors.value.end = t('notificationSettings.errors.time')
    if (errors.value.start || errors.value.end) {
      nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
      return
    }
  }
  saving.value = true
  try {
    apply(await auth.authorizedFetch<NotificationPreferences>('/api/notifications/preferences', {
      method: 'PUT',
      body: {
        types: preferences.value,
        quietHours: {
          enabled: quietEnabled.value,
          start: isValidTime(quietStart.value) ? quietStart.value : '22:00',
          end: isValidTime(quietEnd.value) ? quietEnd.value : '07:00',
          utcOffsetMinutes: currentUtcOffsetMinutes(),
        },
      },
    }))
    message.value = t('notificationSettings.saved')
  }
  catch (error) {
    failure.value = parseApiError(error).kind === 'validation' ? t('notificationSettings.errors.rejected') : t('notificationSettings.errors.save')
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/notifications"
      class="back"
    >
      {{ $t('notificationSettings.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <form
      v-else
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <UiCard class="group">
        <h2>{{ $t('notificationSettings.channels.title') }}</h2>
        <ul class="channels">
          <li>{{ $t('notificationSettings.channels.inApp') }}</li>
          <li>{{ realtime ? $t('notificationSettings.channels.realtimeOn') : $t('notificationSettings.channels.realtimeOff') }}</li>
          <li>{{ emailAvailable ? $t('notificationSettings.channels.emailOn') : $t('notificationSettings.channels.emailOff') }}</li>
          <li>{{ $t('notificationSettings.channels.push') }}</li>
        </ul>
      </UiCard>

      <UiCard class="group">
        <h2>{{ $t('notificationSettings.types.title') }}</h2>
        <p class="hint">
          {{ $t('notificationSettings.types.hint') }}
        </p>
        <fieldset
          v-for="preference in preferences"
          :key="preference.type"
          class="type"
        >
          <legend>{{ $t(`notifications.types.${preference.type}.label`) }}</legend>
          <label class="check">
            <input
              v-model="preference.inApp"
              type="checkbox"
            >
            {{ $t('notificationSettings.inApp') }}
          </label>
          <label class="check">
            <input
              v-model="preference.email"
              type="checkbox"
              :disabled="!emailAvailable"
            >
            {{ $t('notificationSettings.email') }}
          </label>
        </fieldset>
      </UiCard>

      <UiCard class="group">
        <h2>{{ $t('notificationSettings.quiet.title') }}</h2>
        <p class="hint">
          {{ $t('notificationSettings.quiet.hint') }}
        </p>
        <label class="check">
          <input
            v-model="quietEnabled"
            type="checkbox"
          >
          {{ $t('notificationSettings.quiet.enable') }}
        </label>
        <div
          v-if="quietEnabled"
          class="times"
        >
          <UiInput
            v-model="quietStart"
            type="time"
            :label="$t('notificationSettings.quiet.start')"
            :error="errors.start"
          />
          <UiInput
            v-model="quietEnd"
            type="time"
            :label="$t('notificationSettings.quiet.end')"
            :error="errors.end"
          />
        </div>
      </UiCard>

      <p
        class="feedback"
        role="status"
      >
        {{ message }}
      </p>
      <p
        v-if="failure"
        class="feedback error"
        role="alert"
      >
        {{ failure }}
      </p>
      <UiButton
        type="submit"
        block
        :loading="saving"
      >
        {{ $t('notificationSettings.save') }}
      </UiButton>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.group h2 { margin: 0; font-size: var(--font-size-md); }
.hint { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.channels { margin: 0; padding-left: var(--space-4); display: grid; gap: var(--space-1); color: var(--muted); font-size: var(--font-size-sm); }
.type { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-4); border: 0; border-top: 1px solid var(--border); margin: 0; padding: var(--space-3) 0 0; }
.type legend { padding: 0; font-weight: 600; float: left; width: 100%; margin-bottom: var(--space-1); }
.check { display: inline-flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); }
.check input { width: 22px; height: 22px; accent-color: var(--accent); }
.times { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.feedback.error { color: var(--danger); }
</style>
