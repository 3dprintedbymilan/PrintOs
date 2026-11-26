<script setup lang="ts">
import { filterSpools, remainingPercent, summarizeStock } from '~/utils/inventory'
import type { Material, Spool } from '~/utils/inventory'

definePageMeta({ titleKey: 'inventory.title' })

const { t, n } = useI18n()
const auth = useAuthStore()

const spools = ref<Spool[]>([])
const materials = ref<Material[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const materialFilter = ref('')
const search = ref('')
const showArchived = ref(false)

const summary = computed(() => summarizeStock(spools.value))
const visible = computed(() => filterSpools(spools.value, materialFilter.value, search.value))
const materialOptions = computed(() => [
  { value: '', label: t('inventory.allMaterials') },
  ...materials.value.map(material => ({ value: material.id, label: material.name })),
])

async function load() {
  status.value = 'loading'
  try {
    const [loadedSpools, loadedMaterials] = await Promise.all([
      auth.authorizedFetch<Spool[]>('/api/spools', { query: { includeArchived: showArchived.value } }),
      auth.authorizedFetch<Material[]>('/api/materials'),
    ])
    spools.value = loadedSpools
    materials.value = loadedMaterials
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

function subtitle(spool: Spool) {
  return t('inventory.remaining', { grams: n(spool.remainingWeightGrams), percent: remainingPercent(spool) })
}

watch(showArchived, load)
onMounted(load)
</script>

<template>
  <div class="stack">
    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <div class="kpis">
        <UiKpiTile
          :label="$t('inventory.totalStock')"
          :value="`${n(summary.totalKilograms)} kg`"
        />
        <UiKpiTile
          :label="$t('inventory.materials')"
          :value="n(summary.materialCount)"
        />
        <UiKpiTile
          :label="$t('inventory.spools')"
          :value="n(summary.spoolCount)"
        />
      </div>

      <UiButton
        block
        @click="navigateTo('/inventory/new')"
      >
        {{ $t('inventory.addSpool') }}
      </UiButton>

      <template v-if="spools.length > 0">
        <div class="filters">
          <UiInput
            v-model="search"
            :label="$t('inventory.search')"
            type="search"
          />
          <UiSelect
            v-model="materialFilter"
            :label="$t('inventory.material')"
            :options="materialOptions"
          />
        </div>

        <StateEmpty
          v-if="visible.length === 0"
          :message="$t('inventory.noMatches')"
        />
        <UiListRow
          v-for="spool in visible"
          :key="spool.id"
          :to="`/inventory/${spool.id}`"
          :title="`${spool.materialName} · ${spool.colorName}`"
          :subtitle="subtitle(spool)"
        >
          <template #icon>
            <span
              class="swatch"
              :style="{ background: spool.colorHex ?? 'var(--surface-2)' }"
              aria-hidden="true"
            />
          </template>
          <template
            v-if="spool.isArchived"
            #trailing
          >
            <UiStatusChip status="neutral">
              {{ $t('inventory.archived') }}
            </UiStatusChip>
          </template>
        </UiListRow>
      </template>
      <StateEmpty
        v-else
        :message="$t('inventory.empty')"
      />

      <label class="toggle">
        <input
          v-model="showArchived"
          type="checkbox"
        >
        {{ $t('inventory.showArchived') }}
      </label>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--space-2); }
.filters { display: grid; gap: var(--space-2); }
.swatch { display: block; width: 28px; height: 28px; border-radius: var(--radius-pill); border: 1px solid var(--border); }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
