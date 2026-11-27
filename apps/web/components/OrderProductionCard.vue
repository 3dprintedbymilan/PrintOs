<script setup lang="ts">
import { statusTone } from '~/utils/printJobs'
import { lineProgressPercent } from '~/utils/productionQueue'
import type { OrderProduction } from '~/utils/productionQueue'

const props = defineProps<{ orderId: string, status: string, number: number }>()

const { t } = useI18n()
const auth = useAuthStore()

const production = ref<OrderProduction | null>(null)
const loadStatus = ref<'loading' | 'ready' | 'failed'>('loading')
const notice = ref('')
const planOpen = ref(false)

const canPlan = computed(() => ['Confirmed', 'InProduction'].includes(props.status)
  && (production.value?.lines.some(line => line.remaining > 0) ?? false))
const remaining = computed(() => production.value?.lines.reduce((sum, line) => sum + line.remaining, 0) ?? 0)

async function load() {
  loadStatus.value = 'loading'
  try {
    production.value = await auth.authorizedFetch<OrderProduction>(`/api/production/orders/${props.orderId}`)
    loadStatus.value = 'ready'
  }
  catch {
    loadStatus.value = 'failed'
  }
}

async function onCreated(count: number) {
  notice.value = t('queue.plan.created', { count }, count)
  await load()
}

onMounted(load)
watch(() => props.status, load)
</script>

<template>
  <UiCard
    class="production"
    aria-labelledby="order-production"
  >
    <h3 id="order-production">
      {{ $t('queue.order.title') }}
    </h3>
    <StateLoading v-if="loadStatus === 'loading'" />
    <StateError
      v-else-if="loadStatus === 'failed'"
      @retry="load"
    />
    <template v-else-if="production">
      <p
        class="notice"
        role="status"
      >
        {{ notice }}
      </p>
      <div class="overall">
        <strong>{{ $t('queue.order.overall', { produced: production.produced, ordered: production.ordered }) }}</strong>
        <span
          v-if="production.queued > 0"
          class="hint"
        >{{ $t('queue.order.queued', { count: production.queued }, production.queued) }}</span>
      </div>
      <div
        class="bar"
        role="progressbar"
        :aria-label="$t('queue.order.title')"
        aria-valuemin="0"
        aria-valuemax="100"
        :aria-valuenow="production.percent"
      >
        <span :style="{ width: `${production.percent}%` }" />
      </div>
      <p
        v-if="production.failedJobs > 0"
        class="warn"
        role="note"
      >
        {{ $t('queue.order.failed', { count: production.failedJobs }, production.failedJobs) }}
      </p>
      <ul class="lines">
        <li
          v-for="line in production.lines"
          :key="line.lineId"
        >
          <div class="line-head">
            <span class="line-name">{{ line.description }}</span>
            <strong>{{ line.produced }} / {{ line.ordered }}</strong>
          </div>
          <div
            class="bar small"
            role="progressbar"
            :aria-label="line.description"
            aria-valuemin="0"
            aria-valuemax="100"
            :aria-valuenow="lineProgressPercent(line)"
          >
            <span :style="{ width: `${lineProgressPercent(line)}%` }" />
          </div>
          <ul
            v-if="line.jobs.length > 0"
            class="jobs"
          >
            <li
              v-for="job in line.jobs"
              :key="job.id"
            >
              <NuxtLink
                :to="`/print-jobs/${job.id}`"
                class="job"
              >
                <UiStatusChip :status="statusTone(job.status)">
                  {{ $t(`printJobs.status.${job.status}`) }}
                </UiStatusChip>
                <span class="job-name">{{ job.name }}</span>
                <span class="hint">{{ job.status === 'Done' ? job.finishedUnits ?? 0 : job.plannedUnits ?? 0 }}×</span>
              </NuxtLink>
            </li>
          </ul>
          <p
            v-else-if="!['Draft', 'Cancelled'].includes(status)"
            class="hint"
          >
            {{ $t('queue.order.noJobs') }}
          </p>
        </li>
      </ul>
      <UiButton
        v-if="canPlan"
        block
        @click="planOpen = true"
      >
        {{ $t('queue.order.plan') }}
      </UiButton>
      <NuxtLink
        class="queue-link"
        to="/prints?section=queue"
      >
        {{ $t('queue.order.openQueue') }}
      </NuxtLink>
    </template>

    <ProductionPlanDialog
      v-model="planOpen"
      :order-id="orderId"
      :remaining="remaining"
      :title="$t('queue.plan.title', { number })"
      @created="onCreated"
    />
  </UiCard>
</template>

<style scoped>
.production { display: grid; gap: var(--space-3); }
h3 { margin: 0; font-size: var(--font-size-md); }
.overall { display: flex; justify-content: space-between; align-items: baseline; gap: var(--space-3); flex-wrap: wrap; }
.hint { color: var(--muted); font-size: var(--font-size-sm); margin: 0; }
.bar { height: 10px; border-radius: var(--radius-pill); background: var(--surface-2); overflow: hidden; }
.bar.small { height: 6px; }
.bar span { display: block; height: 100%; background: var(--accent); border-radius: inherit; }
.lines { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-4); }
.lines > li { display: grid; gap: var(--space-2); }
.line-head { display: flex; justify-content: space-between; gap: var(--space-3); }
.line-name { overflow-wrap: anywhere; }
.jobs { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-1); }
.job { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--text); text-decoration: none; }
.job-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--font-size-sm); }
.warn { margin: 0; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); background: color-mix(in srgb, var(--warning) 16%, transparent); border-left: 3px solid var(--warning); font-size: var(--font-size-sm); }
.queue-link { color: var(--accent-text); }
.queue-link { display: inline-flex; align-items: center; min-height: var(--tap-size); }
.notice { margin: 0; min-height: 1.2em; color: var(--accent-text); font-size: var(--font-size-sm); }
</style>
