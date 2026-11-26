<script setup lang="ts" generic="Row extends Record<string, unknown>">
defineProps<{
  columns: { key: string, label: string }[]
  rows: Row[]
  caption: string
}>()
</script>

<template>
  <div class="wrap">
    <table>
      <caption class="sr-only">
        {{ caption }}
      </caption>
      <thead>
        <tr>
          <th
            v-for="column in columns"
            :key="column.key"
            scope="col"
          >
            {{ column.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(row, index) in rows"
          :key="index"
        >
          <td
            v-for="column in columns"
            :key="column.key"
          >
            <slot
              :name="`cell-${column.key}`"
              :row="row"
            >
              {{ row[column.key] }}
            </slot>
          </td>
        </tr>
      </tbody>
    </table>
    <StateEmpty v-if="rows.length === 0" />
  </div>
</template>

<style scoped>
.wrap { overflow-x: auto; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
table { width: 100%; border-collapse: collapse; }
th, td { padding: var(--space-3) var(--space-4); text-align: left; white-space: nowrap; }
th { font-size: var(--font-size-sm); color: var(--muted); font-weight: 600; border-bottom: 1px solid var(--border); }
tbody tr + tr td { border-top: 1px solid var(--border); }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
</style>
