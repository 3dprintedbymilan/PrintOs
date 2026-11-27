<script setup lang="ts">
import type { BambuMonitorPrinter } from '~/utils/bambuMonitor'
import { cameraDocsUrl, formatMinutes, hasHealthProblem, relativeAge, windowFor } from '~/utils/bambuMonitor'
import { isActivePrint } from '~/utils/bambu'

const props = defineProps<{ printer: BambuMonitorPrinter, hours: number, now: number }>()
const { t, locale } = useI18n()

const utilization = computed(() => windowFor(props.printer, props.hours))
const percent = computed(() => utilization.value?.printingPercent ?? null)
const seen = computed(() => (props.printer.lastSeenAt ? relativeAge(props.printer.lastSeenAt, props.now, locale.value) : t('bambu.never')))
const lastKnown = computed(() => (props.printer.lastKnownState ? t(`bambu.state.${props.printer.lastKnownState}`) : t('monitor.unknownState')))
const notice = computed(() => {
  if (!props.printer.online) return t('monitor.offlineNote', { state: lastKnown.value, age: seen.value })
  if (props.printer.stale) return t('monitor.staleNote', { age: seen.value })
  return ''
})
const timeFormat = computed(() => new Intl.DateTimeFormat(locale.value, { weekday: 'short', hour: '2-digit', minute: '2-digit' }))

function when(iso: string) {
  return timeFormat.value.format(new Date(iso))
}
</script>

<template>
  <UiCard class="monitor">
    <header class="head">
      <div class="title">
        <h2>{{ printer.printerName }}</h2>
        <p class="model">
          {{ printer.model }}
        </p>
      </div>
      <BambuStateChip :state="printer.state" />
    </header>

    <p
      v-if="notice"
      class="notice"
      role="status"
    >
      {{ notice }}
    </p>

    <section
      v-if="isActivePrint(printer) || (printer.stale && printer.jobName)"
      class="block"
      :class="{ faded: printer.stale }"
    >
      <p class="job">
        {{ printer.jobName ?? $t('bambu.now.unnamed') }}
      </p>
      <BambuProgress
        :printer="printer"
        detailed
      />
    </section>

    <section
      class="block"
      :aria-label="$t('monitor.utilization.title')"
    >
      <h3>{{ $t('monitor.utilization.title') }}</h3>
      <template v-if="percent !== null && utilization">
        <p class="big">
          {{ percent }}%
        </p>
        <div
          class="bar"
          role="img"
          :aria-label="$t('monitor.utilization.value', { percent })"
        >
          <span
            class="fill"
            :style="{ width: `${percent}%` }"
          />
        </div>
        <p class="meta">
          {{ $t('monitor.utilization.detail', { printing: formatMinutes(utilization.printingMinutes), observed: formatMinutes(utilization.observedMinutes) }) }}
        </p>
        <p
          v-if="utilization.coveragePercent < 95"
          class="meta"
        >
          {{ $t('monitor.utilization.coverage', { percent: utilization.coveragePercent }) }}
        </p>
      </template>
      <p
        v-else
        class="meta"
      >
        {{ $t('monitor.utilization.none') }}
      </p>
    </section>

    <section
      class="block"
      :aria-label="$t('monitor.health.title')"
    >
      <h3>{{ $t('monitor.health.title') }}</h3>
      <dl class="facts">
        <div>
          <dt>{{ $t('monitor.health.connection') }}</dt>
          <dd>{{ printer.online ? $t('monitor.health.online') : $t('monitor.health.offline') }}</dd>
        </div>
        <div>
          <dt>{{ $t('monitor.health.lastSeen') }}</dt>
          <dd>{{ seen }}</dd>
        </div>
        <div>
          <dt>{{ $t('monitor.health.nozzle') }}</dt>
          <dd>{{ printer.nozzleTemperature !== null ? `${Math.round(printer.nozzleTemperature)} °C` : '–' }}</dd>
        </div>
        <div>
          <dt>{{ $t('monitor.health.bed') }}</dt>
          <dd>{{ printer.bedTemperature !== null ? `${Math.round(printer.bedTemperature)} °C` : '–' }}</dd>
        </div>
      </dl>

      <ul
        v-if="hasHealthProblem(printer)"
        class="problems"
        role="alert"
      >
        <li v-if="printer.printErrorCode">
          {{ $t('monitor.health.errorCode', { code: printer.printErrorCode }) }}
        </li>
        <li
          v-for="code in printer.hmsCodes"
          :key="code"
        >
          {{ $t('monitor.health.hms', { code }) }}
        </li>
      </ul>
      <p
        v-else-if="printer.online"
        class="meta"
      >
        {{ $t('monitor.health.noErrors') }}
      </p>

      <ul
        v-if="printer.trays.length"
        class="trays"
        :aria-label="$t('monitor.health.filament')"
      >
        <li
          v-for="tray in printer.trays"
          :key="tray.id"
          :class="{ current: tray.current }"
        >
          <span
            class="swatch"
            :style="{ background: tray.color ?? 'transparent' }"
            aria-hidden="true"
          />
          <span>{{ tray.type || $t('monitor.health.emptyTray') }}</span>
          <span
            v-if="tray.current"
            class="meta"
          >{{ $t('monitor.health.inUse') }}</span>
        </li>
      </ul>
    </section>

    <section
      class="block"
      :aria-label="$t('monitor.timeline.title')"
    >
      <h3>{{ $t('monitor.timeline.title') }}</h3>
      <p
        v-if="printer.timeline.length === 0"
        class="meta"
      >
        {{ $t('monitor.timeline.empty') }}
      </p>
      <ol
        v-else
        class="timeline"
      >
        <li
          v-for="entry in printer.timeline"
          :key="entry.at"
        >
          <BambuStateChip :state="entry.state" />
          <span class="entry">
            <span class="when">{{ when(entry.at) }}<template v-if="entry.durationMinutes !== null"> · {{ formatMinutes(entry.durationMinutes) }}</template></span>
            <span
              v-if="entry.jobName && entry.state === 'printing'"
              class="meta"
            >{{ entry.jobName }}</span>
          </span>
        </li>
      </ol>
    </section>

    <section
      class="block camera"
      :aria-label="$t('monitor.camera.title')"
    >
      <h3>{{ $t('monitor.camera.title') }}</h3>
      <p class="unavailable">
        {{ $t('monitor.camera.unavailable') }}
      </p>
      <p class="meta">
        {{ $t('monitor.camera.explain') }}
      </p>
      <a
        :href="cameraDocsUrl"
        target="_blank"
        rel="noopener noreferrer"
      >{{ $t('monitor.camera.docs') }}</a>
    </section>
  </UiCard>
</template>

<style scoped>
.monitor { display: grid; gap: var(--space-4); }
.head { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-3); }
.title { min-width: 0; }
h2 { margin: 0; font-size: var(--font-size-lg); overflow-wrap: anywhere; }
h3 { margin: 0; font-size: var(--font-size-sm); font-weight: 600; letter-spacing: 0.04em; text-transform: uppercase; color: var(--muted); }
.model, .meta { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.notice { margin: 0; padding: var(--space-3); color: var(--text); background: color-mix(in srgb, var(--warning) 16%, var(--surface)); border: 1px solid color-mix(in srgb, var(--warning) 50%, var(--border)); border-radius: var(--radius-md); font-size: var(--font-size-sm); }
.block { display: grid; gap: var(--space-2); padding-top: var(--space-4); border-top: 1px solid var(--border); }
.faded { opacity: 0.7; }
.job { margin: 0; font-weight: 600; overflow-wrap: anywhere; }
.big { margin: 0; font-size: var(--font-size-xl); font-weight: 700; font-variant-numeric: tabular-nums; }
.bar { height: 8px; overflow: hidden; background: var(--surface-2); border-radius: var(--radius-pill); }
.fill { display: block; height: 100%; background: var(--accent); border-radius: var(--radius-pill); }
.facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); margin: 0; }
.facts div { display: grid; gap: 2px; }
dt { color: var(--muted); font-size: var(--font-size-sm); }
dd { margin: 0; font-weight: 600; font-variant-numeric: tabular-nums; }
.problems { display: grid; gap: var(--space-1); margin: 0; padding: var(--space-3); list-style: none; color: var(--text); background: color-mix(in srgb, var(--danger) 14%, var(--surface)); border: 1px solid color-mix(in srgb, var(--danger) 50%, var(--border)); border-radius: var(--radius-md); font-size: var(--font-size-sm); font-variant-numeric: tabular-nums; }
.trays { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.trays li { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius-pill); font-size: var(--font-size-sm); }
.trays li.current { border-color: var(--accent); }
.swatch { width: 12px; height: 12px; border: 1px solid var(--border); border-radius: 50%; }
.timeline { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.timeline li { display: flex; align-items: center; gap: var(--space-3); min-height: 36px; }
.entry { display: grid; min-width: 0; }
.when { font-size: var(--font-size-sm); font-variant-numeric: tabular-nums; }
.unavailable { margin: 0; font-weight: 600; }
.camera a { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); font-weight: 600; }
</style>
