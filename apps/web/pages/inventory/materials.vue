<script setup lang="ts">
import { validateThresholds } from '~/utils/inventory'
import type { Material } from '~/utils/inventory'

definePageMeta({ titleKey: 'alerts.materialsTitle' })

const { t } = useI18n()
const auth = useAuthStore()

interface Row {
  material: Material
  low: string
  reorder: string
  lowError: string
  reorderError: string
  message: string
  failed: boolean
  saving: boolean
}

const rows = ref<Row[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')

function toRow(material: Material): Row {
  return {
    material,
    low: material.lowStockGrams?.toString() ?? '',
    reorder: material.reorderGrams?.toString() ?? '',
    lowError: '',
    reorderError: '',
    message: '',
    failed: false,
    saving: false,
  }
}

async function load() {
  status.value = 'loading'
  try {
    rows.value = (await auth.authorizedFetch<Material[]>('/api/materials')).map(toRow)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function save(row: Row) {
  row.message = ''
  row.failed = false
  const { thresholds, errors } = validateThresholds(row.low, row.reorder)
  row.lowError = errors.low ? t('alerts.errors.number') : ''
  row.reorderError = errors.reorder ? t(`alerts.errors.${errors.reorder}`) : ''
  if (!thresholds) return

  row.saving = true
  try {
    row.material = await auth.authorizedFetch<Material>(`/api/materials/${row.material.id}/thresholds`, { method: 'PUT', body: thresholds })
    row.message = t('alerts.saved')
  }
  catch {
    row.failed = true
    row.message = t('alerts.errors.save')
  }
  finally {
    row.saving = false
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/inventory"
      class="back"
    >
      {{ $t('inventory.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <p class="intro">
        {{ $t('alerts.intro') }}
      </p>
      <StateEmpty
        v-if="rows.length === 0"
        :message="$t('alerts.noMaterials')"
      />
      <form
        v-for="row in rows"
        :key="row.material.id"
        novalidate
        @submit.prevent="save(row)"
      >
        <UiCard class="group">
          <h2>{{ row.material.name }}</h2>
          <UiInput
            v-model="row.low"
            :label="$t('alerts.lowStock')"
            :hint="$t('alerts.lowStockHint')"
            :error="row.lowError"
            inputmode="numeric"
          />
          <UiInput
            v-model="row.reorder"
            :label="$t('alerts.reorder')"
            :hint="$t('alerts.reorderHint')"
            :error="row.reorderError"
            inputmode="numeric"
          />
          <p
            class="feedback"
            :class="{ failed: row.failed }"
            :role="row.failed ? 'alert' : 'status'"
          >
            {{ row.message }}
          </p>
          <UiButton
            type="submit"
            variant="secondary"
            block
            :loading="row.saving"
          >
            {{ $t('alerts.save') }}
          </UiButton>
        </UiCard>
      </form>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.group { display: grid; gap: var(--space-3); }
.group h2 { margin: 0; font-size: var(--font-size-md); }
.feedback { margin: 0; min-height: 1.25em; color: var(--accent); font-size: var(--font-size-sm); }
.feedback.failed { color: var(--danger); }
</style>
