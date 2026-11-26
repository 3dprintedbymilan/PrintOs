<script setup lang="ts">
definePageMeta({ layout: false, public: true })
const range = ref('today')
const name = ref('')
const material = ref('pla')
const dialogOpen = ref(false)

const rows = [
  { material: 'PLA', color: 'Green', remaining: '0.8 kg' },
  { material: 'PETG', color: 'Black', remaining: '1.5 kg' },
]
const columns = [
  { key: 'material', label: 'Material' },
  { key: 'color', label: 'Color' },
  { key: 'remaining', label: 'Remaining' },
]
</script>

<template>
  <main class="page">
    <h1>Design system</h1>
    <UiThemeSwitch />

    <h2>Segmented control</h2>
    <UiSegmentedControl
      v-model="range"
      label="Range"
      :options="[{ value: 'today', label: 'Today' }, { value: '7d', label: '7 Days' }, { value: '30d', label: '30 Days' }]"
    />

    <h2>KPI tiles</h2>
    <div class="grid">
      <UiKpiTile
        label="Revenue"
        value="€12,840"
        delta="+8.4%"
      />
      <UiKpiTile
        label="Material cost"
        value="€3,210"
        delta="-4.1%"
        trend="down"
      />
    </div>

    <h2>Buttons</h2>
    <div class="row">
      <UiButton>Primary</UiButton>
      <UiButton variant="secondary">
        Secondary
      </UiButton>
      <UiButton variant="ghost">
        Ghost
      </UiButton>
      <UiButton variant="danger">
        Danger
      </UiButton>
      <UiButton loading>
        Loading
      </UiButton>
      <UiButton disabled>
        Disabled
      </UiButton>
    </div>

    <h2>Inputs</h2>
    <UiCard>
      <div class="stack">
        <UiInput
          v-model="name"
          label="Spool name"
          hint="Shown in the inventory list"
        />
        <UiInput
          label="Weight (g)"
          inputmode="numeric"
          error="Enter a number"
        />
        <UiSelect
          v-model="material"
          label="Material"
          :options="[{ value: 'pla', label: 'PLA' }, { value: 'petg', label: 'PETG' }]"
        />
      </div>
    </UiCard>

    <h2>Status chips</h2>
    <div class="row">
      <UiStatusChip status="success">
        In stock
      </UiStatusChip>
      <UiStatusChip status="warning">
        Reorder soon
      </UiStatusChip>
      <UiStatusChip status="danger">
        Low stock
      </UiStatusChip>
      <UiStatusChip status="info">
        Printing
      </UiStatusChip>
      <UiStatusChip>Archived</UiStatusChip>
    </div>

    <h2>List rows</h2>
    <div class="stack">
      <UiListRow
        title="Filament Stock"
        subtitle="8 materials · 32 spools"
        to="/design"
      />
      <UiListRow
        title="PLA - Low stock"
        subtitle="0.8 kg"
      >
        <template #trailing>
          <UiStatusChip status="danger">
            Low
          </UiStatusChip>
        </template>
      </UiListRow>
    </div>

    <h2>Table</h2>
    <UiTable
      caption="Filament stock"
      :columns="columns"
      :rows="rows"
    />

    <h2>Dialog</h2>
    <UiButton
      variant="secondary"
      @click="dialogOpen = true"
    >
      Open dialog
    </UiButton>
    <UiDialog
      v-model="dialogOpen"
      title="Adjust stock"
    >
      <p>Confirm the stock adjustment.</p>
      <template #actions>
        <UiButton
          variant="ghost"
          @click="dialogOpen = false"
        >
          Cancel
        </UiButton>
        <UiButton @click="dialogOpen = false">
          Save
        </UiButton>
      </template>
    </UiDialog>

    <h2>States</h2>
    <UiCard :padded="false">
      <StateLoading />
      <StateEmpty />
      <StateError />
    </UiCard>
  </main>
</template>

<style scoped>
.page { display: grid; gap: var(--space-4); max-width: 720px; margin: 0 auto; padding: var(--space-4); }
h1, h2 { margin: 0; }
h2 { margin-top: var(--space-3); font-size: var(--font-size-lg); }
.grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
.row { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.stack { display: grid; gap: var(--space-3); }
</style>
