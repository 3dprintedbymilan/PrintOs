<script setup lang="ts">
import { utilizationWindows } from '~/utils/bambuMonitor'

definePageMeta({ titleKey: 'monitor.title' })

const { t } = useI18n()
const { monitor, state, refreshFailed, now, reload } = useBambuMonitor()

const windowKey = ref('24')
const hours = computed(() => Number(windowKey.value))
const windowOptions = computed(() => utilizationWindows.map(value => ({ value: String(value), label: t(`monitor.windows.${value}`) })))
const needsReconnect = computed(() => monitor.value?.connectionStatus === 'tokenExpired')
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/printers"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <StateLoading v-if="state === 'loading'" />
    <StateError
      v-else-if="state === 'failed'"
      @retry="reload"
    />
    <template v-else-if="monitor">
      <UiCard v-if="!monitor.connected">
        <p class="calm">
          {{ $t('monitor.notConnected') }}
        </p>
        <NuxtLink
          to="/printers/bambu"
          class="link"
        >
          {{ $t('bambu.now.connect') }}
        </NuxtLink>
      </UiCard>
      <template v-else>
        <p
          v-if="needsReconnect"
          class="alert"
          role="alert"
        >
          {{ $t('bambu.errors.expiredShort') }}
          <NuxtLink to="/printers/bambu">
            {{ $t('bambu.reconnect') }}
          </NuxtLink>
        </p>
        <p
          v-if="refreshFailed"
          class="alert"
          role="status"
        >
          {{ $t('monitor.refreshFailed') }}
        </p>

        <UiCard v-if="monitor.printers.length === 0">
          <p class="calm">
            {{ $t('bambu.now.noPrinters') }}
          </p>
          <NuxtLink
            to="/printers/bambu"
            class="link"
          >
            {{ $t('bambu.now.link') }}
          </NuxtLink>
        </UiCard>
        <template v-else>
          <UiSegmentedControl
            v-model="windowKey"
            :label="$t('monitor.windowLabel')"
            :options="windowOptions"
          />
          <PrinterMonitorCard
            v-for="printer in monitor.printers"
            :key="printer.printerId"
            :printer="printer"
            :hours="hours"
            :now="now"
          />
        </template>
      </template>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-4); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.calm { margin: 0 0 var(--space-2); color: var(--muted); }
.alert { margin: 0; color: var(--warning); font-size: var(--font-size-sm); }
.alert a, .link { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; }
</style>
