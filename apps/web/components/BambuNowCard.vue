<script setup lang="ts">
import { isActivePrint } from '~/utils/bambu'

const { status, state, reload } = useBambuStatus()

const active = computed(() => (status.value?.printers ?? []).filter(isActivePrint))
const others = computed(() => (status.value?.printers ?? []).filter(printer => !isActivePrint(printer)))
const needsReconnect = computed(() => status.value?.connectionStatus === 'tokenExpired')
</script>

<template>
  <UiCard class="now">
    <h2>{{ $t('bambu.now.title') }}</h2>
    <StateLoading v-if="state === 'loading'" />
    <StateError
      v-else-if="state === 'failed'"
      @retry="reload"
    />
    <template v-else-if="status">
      <template v-if="!status.connected">
        <p class="calm">
          {{ $t('bambu.now.notConnected') }}
        </p>
        <NuxtLink
          to="/printers/bambu"
          class="link"
        >
          {{ $t('bambu.now.connect') }}
        </NuxtLink>
      </template>
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
          v-else-if="status.printers.length === 0"
          class="calm"
        >
          {{ $t('bambu.now.noPrinters') }}
          <NuxtLink to="/printers/bambu">
            {{ $t('bambu.now.link') }}
          </NuxtLink>
        </p>
        <p
          v-else-if="active.length === 0"
          class="calm"
        >
          {{ $t('bambu.now.nothing') }}
        </p>

        <section
          v-for="printer in active"
          :key="printer.printerId"
          class="job"
        >
          <header>
            <strong class="name">{{ printer.jobName ?? $t('bambu.now.unnamed') }}</strong>
            <BambuStateChip :state="printer.state" />
          </header>
          <p class="where">
            {{ printer.printerName }}
          </p>
          <BambuProgress
            :printer="printer"
            detailed
          />
        </section>

        <ul
          v-if="others.length"
          class="others"
        >
          <li
            v-for="printer in others"
            :key="printer.printerId"
          >
            <span>{{ printer.printerName }}</span>
            <BambuStateChip :state="printer.state" />
          </li>
        </ul>
      </template>
    </template>
  </UiCard>
</template>

<style scoped>
.now { display: grid; gap: var(--space-3); }
h2 { margin: 0; font-size: var(--font-size-md); }
.calm { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.alert { margin: 0; color: var(--warning); font-size: var(--font-size-sm); }
a, .link { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; }
.job { display: grid; gap: var(--space-2); }
.job header { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
.name { overflow-wrap: anywhere; }
.where { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.others { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.others li { display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); min-height: 36px; }
</style>
