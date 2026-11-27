<script setup lang="ts">
import type { Customer } from '~/utils/customers'
import { customerContactLine } from '~/utils/customers'

definePageMeta({ titleKey: 'customers.title' })

const auth = useAuthStore()

const customers = ref<Customer[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const showArchived = ref(false)
const search = ref('')

async function load() {
  status.value = 'loading'
  try {
    customers.value = await auth.authorizedFetch<Customer[]>('/api/customers', {
      query: { includeArchived: showArchived.value, search: search.value.trim() || undefined },
    })
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

let timer: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
watch(showArchived, load)
onMounted(load)
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <UiInput
      v-model="search"
      type="search"
      :label="$t('customers.search')"
      :placeholder="$t('customers.searchPlaceholder')"
    />

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="customers.length === 0"
        :message="search.trim() ? $t('customers.noResults') : $t('customers.empty')"
      />
      <UiListRow
        v-for="customer in customers"
        :key="customer.id"
        :to="`/customers/${customer.id}`"
        :title="customer.name"
        :subtitle="customerContactLine(customer) || undefined"
      >
        <template
          v-if="customer.isArchived"
          #trailing
        >
          <UiStatusChip status="neutral">
            {{ $t('customers.archived') }}
          </UiStatusChip>
        </template>
      </UiListRow>
    </template>

    <label class="toggle">
      <input
        v-model="showArchived"
        type="checkbox"
      >
      {{ $t('customers.showArchived') }}
    </label>

    <UiButton
      block
      @click="navigateTo('/customers/new')"
    >
      {{ $t('customers.add') }}
    </UiButton>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.toggle { display: flex; align-items: center; gap: var(--space-2); min-height: var(--tap-size); color: var(--muted); font-size: var(--font-size-sm); }
.toggle input { width: 20px; height: 20px; accent-color: var(--accent); }
</style>
