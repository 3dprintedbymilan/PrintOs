<script setup lang="ts">
import type { BambuConnection, BambuDevice } from '~/utils/bambu'
import { validateToken } from '~/utils/bambu'
import type { Printer } from '~/utils/printerForm'
import { parseApiError } from '~/utils/apiError'

definePageMeta({ titleKey: 'bambu.title' })

const { t, d } = useI18n()
const auth = useAuthStore()

const NEW_PRINTER = 'new'

const connection = ref<BambuConnection | null>(null)
const printers = ref<Printer[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

const token = ref('')
const region = ref('global')
const tokenError = ref('')
const formError = ref('')
const connecting = ref(false)

const choices = reactive<Record<string, string>>({})
const busySerial = ref('')
const refreshing = ref(false)
const notice = ref('')
const confirmingDisconnect = ref(false)
const disconnecting = ref(false)

const needsToken = computed(() => !connection.value?.connected || connection.value.status === 'tokenExpired')
const regionOptions = computed(() => [
  { value: 'global', label: t('bambu.connect.regions.global') },
  { value: 'china', label: t('bambu.connect.regions.china') },
])
const lastSync = computed(() => (connection.value?.lastSyncAt ? d(new Date(connection.value.lastSyncAt), { dateStyle: 'medium', timeStyle: 'short' }) : t('bambu.never')))

function printerOptions(device: BambuDevice) {
  const taken = new Set((connection.value?.devices ?? []).filter(other => other.serial !== device.serial && other.printerId).map(other => other.printerId))
  return [
    { value: NEW_PRINTER, label: t('bambu.devices.createNew') },
    ...printers.value.filter(printer => !taken.has(printer.id)).map(printer => ({ value: printer.id, label: printer.name })),
  ]
}

async function load() {
  status.value = 'loading'
  try {
    const [loaded, list] = await Promise.all([
      auth.authorizedFetch<BambuConnection>('/api/bambu/connection'),
      auth.authorizedFetch<Printer[]>('/api/printers'),
    ])
    connection.value = loaded
    printers.value = list
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function showError(error: unknown, fallbackKey: string) {
  const parsed = parseApiError(error)
  if (parsed.kind === 'validation' && parsed.fieldErrors.accessToken) {
    tokenError.value = t('bambu.errors.rejected')
    return
  }
  if (parsed.status === 502) {
    formError.value = t('bambu.errors.unreachable')
    return
  }
  formError.value = t(fallbackKey)
}

async function connect() {
  tokenError.value = ''
  formError.value = ''
  const problem = validateToken(token.value)
  if (problem) {
    tokenError.value = t(`bambu.errors.${problem}`)
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
    return
  }

  connecting.value = true
  try {
    connection.value = await auth.authorizedFetch<BambuConnection>('/api/bambu/connection', {
      method: 'PUT',
      body: { accessToken: token.value, region: region.value },
    })
    token.value = ''
    notice.value = t('bambu.connected.done')
  }
  catch (error) {
    showError(error, 'bambu.errors.generic')
    nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
  }
  finally {
    connecting.value = false
  }
}

async function link(device: BambuDevice) {
  const choice = choices[device.serial] || NEW_PRINTER
  busySerial.value = device.serial
  formError.value = ''
  try {
    connection.value = await auth.authorizedFetch<BambuConnection>(`/api/bambu/devices/${encodeURIComponent(device.serial)}/link`, {
      method: 'POST',
      body: choice === NEW_PRINTER ? { createPrinter: true } : { printerId: choice, createPrinter: false },
    })
    printers.value = await auth.authorizedFetch<Printer[]>('/api/printers')
    notice.value = t('bambu.devices.linked')
  }
  catch {
    formError.value = t('bambu.errors.link')
  }
  finally {
    busySerial.value = ''
  }
}

async function unlink(device: BambuDevice) {
  busySerial.value = device.serial
  formError.value = ''
  try {
    connection.value = await auth.authorizedFetch<BambuConnection>(`/api/bambu/devices/${encodeURIComponent(device.serial)}/link`, { method: 'DELETE' })
    notice.value = t('bambu.devices.unlinked')
  }
  catch {
    formError.value = t('bambu.errors.link')
  }
  finally {
    busySerial.value = ''
  }
}

async function refresh() {
  refreshing.value = true
  formError.value = ''
  try {
    connection.value = await auth.authorizedFetch<BambuConnection>('/api/bambu/connection/refresh', { method: 'POST' })
    notice.value = t('bambu.connected.refreshed')
  }
  catch {
    formError.value = t('bambu.errors.generic')
  }
  finally {
    refreshing.value = false
  }
}

async function disconnect() {
  disconnecting.value = true
  try {
    await auth.authorizedFetch('/api/bambu/connection', { method: 'DELETE' })
    connection.value = { connected: false, region: null, status: null, accountName: null, connectedAt: null, lastSyncAt: null, flaggedImports: 0, devices: [] }
    confirmingDisconnect.value = false
    notice.value = t('bambu.disconnect.done')
  }
  catch {
    confirmingDisconnect.value = false
    formError.value = t('bambu.errors.generic')
  }
  finally {
    disconnecting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/printers"
      class="back"
    >
      {{ $t('printers.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="connection">
      <p
        v-if="notice"
        class="notice"
        role="status"
      >
        {{ notice }}
      </p>
      <p
        v-if="formError"
        class="error"
        role="alert"
      >
        {{ formError }}
      </p>

      <UiCard
        v-if="connection.connected && connection.status === 'tokenExpired'"
        class="group problem"
      >
        <h2>{{ $t('bambu.errors.expiredTitle') }}</h2>
        <p>{{ $t('bambu.errors.expiredBody') }}</p>
      </UiCard>
      <UiCard
        v-else-if="connection.connected && connection.status === 'unreachable'"
        class="group problem"
      >
        <h2>{{ $t('bambu.errors.unreachableTitle') }}</h2>
        <p>{{ $t('bambu.errors.unreachable') }}</p>
        <UiButton
          variant="secondary"
          :loading="refreshing"
          @click="refresh"
        >
          {{ $t('bambu.connected.retry') }}
        </UiButton>
      </UiCard>

      <UiCard
        v-if="connection.connected && !needsToken"
        class="group"
      >
        <header class="summary">
          <h2>{{ $t('bambu.connected.heading') }}</h2>
          <UiStatusChip status="success">
            {{ $t('bambu.connected.chip') }}
          </UiStatusChip>
        </header>
        <p
          v-if="connection.accountName"
          class="muted"
        >
          {{ $t('bambu.connected.account', { name: connection.accountName }) }}
        </p>
        <p class="muted">
          {{ $t('bambu.connected.lastSync', { time: lastSync }) }}
        </p>
        <p
          v-if="connection.flaggedImports > 0"
          class="flag"
        >
          {{ $t('bambu.connected.flagged', { count: connection.flaggedImports }) }}
        </p>
        <UiButton
          variant="secondary"
          :loading="refreshing"
          @click="refresh"
        >
          {{ $t('bambu.connected.refresh') }}
        </UiButton>
      </UiCard>

      <form
        v-if="needsToken"
        class="form"
        novalidate
        @submit.prevent="connect"
      >
        <UiCard class="group">
          <h2>{{ connection.connected ? $t('bambu.reconnect') : $t('bambu.connect.heading') }}</h2>
          <p class="muted">
            {{ $t('bambu.connect.intro') }}
          </p>
          <details class="guide">
            <summary>{{ $t('bambu.connect.guideTitle') }}</summary>
            <ol>
              <li>{{ $t('bambu.connect.step1') }}</li>
              <li>{{ $t('bambu.connect.step2') }}</li>
              <li>{{ $t('bambu.connect.step3') }}</li>
              <li>{{ $t('bambu.connect.step4') }}</li>
            </ol>
            <p class="muted">
              {{ $t('bambu.connect.guideNote') }}
            </p>
          </details>
          <UiInput
            v-model="token"
            type="password"
            revealable
            autocomplete="off"
            name="bambu-access-token"
            :label="$t('bambu.connect.tokenLabel')"
            :hint="$t('bambu.connect.tokenHint')"
            :error="tokenError"
            :show-label="$t('bambu.connect.show')"
            :hide-label="$t('bambu.connect.hide')"
          />
          <UiSelect
            v-model="region"
            :label="$t('bambu.connect.region')"
            :options="regionOptions"
          />
          <UiButton
            type="submit"
            block
            :loading="connecting"
          >
            {{ connection.connected ? $t('bambu.reconnect') : $t('bambu.connect.submit') }}
          </UiButton>
        </UiCard>
      </form>

      <section
        v-if="connection.connected"
        class="devices"
        aria-labelledby="bambu-devices"
      >
        <h2 id="bambu-devices">
          {{ $t('bambu.devices.heading') }}
        </h2>
        <p
          v-if="connection.devices.length === 0"
          class="muted"
        >
          {{ $t('bambu.devices.empty') }}
        </p>
        <UiCard
          v-for="device in connection.devices"
          :key="device.serial"
          class="device"
        >
          <header class="summary">
            <div class="text">
              <strong>{{ device.name }}</strong>
              <span class="muted">{{ device.model }} · {{ device.serial }}</span>
            </div>
            <UiStatusChip :status="device.online ? 'success' : 'neutral'">
              {{ device.online ? $t('bambu.devices.online') : $t('bambu.devices.offline') }}
            </UiStatusChip>
          </header>

          <template v-if="device.printerId">
            <p class="muted">
              {{ $t('bambu.devices.linkedTo', { printer: device.printerName }) }}
            </p>
            <UiButton
              variant="secondary"
              :loading="busySerial === device.serial"
              @click="unlink(device)"
            >
              {{ $t('bambu.devices.unlink') }}
            </UiButton>
          </template>
          <template v-else>
            <UiSelect
              v-model="choices[device.serial]"
              :label="$t('bambu.devices.linkLabel', { name: device.name })"
              :options="printerOptions(device)"
            />
            <UiButton
              :loading="busySerial === device.serial"
              @click="link(device)"
            >
              {{ $t('bambu.devices.link') }}
            </UiButton>
          </template>
        </UiCard>
      </section>

      <UiButton
        v-if="connection.connected"
        variant="ghost"
        block
        @click="confirmingDisconnect = true"
      >
        {{ $t('bambu.disconnect.button') }}
      </UiButton>
    </template>

    <UiDialog
      v-model="confirmingDisconnect"
      :title="$t('bambu.disconnect.title')"
    >
      <p>{{ $t('bambu.disconnect.body') }}</p>
      <template #actions>
        <UiButton
          variant="secondary"
          @click="confirmingDisconnect = false"
        >
          {{ $t('bambu.disconnect.cancel') }}
        </UiButton>
        <UiButton
          variant="danger"
          :loading="disconnecting"
          @click="disconnect"
        >
          {{ $t('bambu.disconnect.confirm') }}
        </UiButton>
      </template>
    </UiDialog>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.form, .group, .device { display: grid; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-md); }
p { margin: 0; }
.muted { color: var(--muted); font-size: var(--font-size-sm); }
.notice { color: var(--success); font-size: var(--font-size-sm); }
.error { color: var(--danger); font-size: var(--font-size-sm); }
.flag { color: var(--warning); font-size: var(--font-size-sm); }
.problem { border-color: var(--warning); }
.summary { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
.text { display: grid; min-width: 0; }
.text strong, .text span { overflow-wrap: anywhere; }
.devices { display: grid; gap: var(--space-3); }
.guide summary { display: flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; cursor: pointer; }
.guide ol { margin: 0 0 var(--space-2); padding-left: var(--space-5); display: grid; gap: var(--space-2); font-size: var(--font-size-sm); }
</style>
