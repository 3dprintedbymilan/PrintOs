<script setup lang="ts">
import { describeChanges, hasMorePages } from '~/utils/audit'
import type { AuditEntry, AuditLabels, AuditPage } from '~/utils/audit'

definePageMeta({ titleKey: 'audit.title' })

const { t, te, d, n } = useI18n()
const auth = useAuthStore()

const entries = ref<AuditEntry[]>([])
const entityTypes = ref<string[]>([])
const entityType = ref('')
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const loadingMore = ref(false)
const more = ref(false)
const page = ref(1)
const pageSize = 25

const typeOptions = computed(() => [
  { value: '', label: t('audit.allTypes') },
  ...entityTypes.value.map(type => ({ value: type, label: entityLabel(type) })),
])

function entityLabel(type: string) {
  return te(`audit.entities.${type}`) ? t(`audit.entities.${type}`) : type
}

function actionLabel(action: string) {
  return te(`audit.actions.${action}`) ? t(`audit.actions.${action}`) : action
}

async function fetchPage(target: number): Promise<AuditPage> {
  const query: Record<string, string | number> = { page: target, pageSize }
  if (entityType.value) query.entityType = entityType.value
  return auth.authorizedFetch<AuditPage>('/api/audit', { query })
}

async function load() {
  status.value = 'loading'
  try {
    const [first, types] = await Promise.all([
      fetchPage(1),
      auth.authorizedFetch<string[]>('/api/audit/entity-types'),
    ])
    entries.value = first.items
    entityTypes.value = types
    page.value = 1
    more.value = hasMorePages(first)
    status.value = 'ready'
  }
  catch {
    status.value = 'failed'
  }
}

async function loadMore() {
  loadingMore.value = true
  try {
    const next = await fetchPage(page.value + 1)
    entries.value = [...entries.value, ...next.items]
    page.value = next.page
    more.value = hasMorePages(next)
  }
  catch {
    status.value = 'failed'
  }
  finally {
    loadingMore.value = false
  }
}

const labels: AuditLabels = {
  field: name => (te(`audit.fields.${name}`) ? t(`audit.fields.${name}`) : undefined),
  value: raw => (te(`audit.values.${raw}`) ? t(`audit.values.${raw}`) : undefined),
  boolean: value => t(value ? 'audit.values.true' : 'audit.values.false'),
  dateTime: iso => d(new Date(iso), { dateStyle: 'medium', timeStyle: 'short' }),
  number: value => n(value),
}

function actor(entry: AuditEntry) {
  return entry.actorEmail ?? t('audit.system')
}

watch(entityType, load)
onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/business"
      class="back"
    >
      {{ $t('settings.back') }}
    </NuxtLink>

    <p class="intro">
      {{ $t('audit.intro') }}
    </p>

    <UiSelect
      v-model="entityType"
      :label="$t('audit.filter')"
      :options="typeOptions"
    />

    <StateLoading v-if="status === 'loading'" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="entries.length === 0"
        :message="$t('audit.empty')"
      />
      <ul
        v-else
        class="entries"
      >
        <li
          v-for="entry in entries"
          :key="entry.id"
        >
          <UiCard class="entry">
            <p class="headline">
              <strong>{{ entityLabel(entry.entityType) }}</strong>
              <UiStatusChip :status="entry.action === 'Deleted' ? 'danger' : 'neutral'">
                {{ actionLabel(entry.action) }}
              </UiStatusChip>
            </p>
            <p class="meta">
              {{ actor(entry) }} · {{ d(new Date(entry.createdAt), { dateStyle: 'medium', timeStyle: 'short' }) }}
            </p>
            <ul class="changes">
              <li
                v-for="line in describeChanges(entry, labels)"
                :key="line.field"
              >
                <span class="field">{{ line.field }}</span>
                <span class="values">{{ line.before }} → {{ line.after }}</span>
              </li>
            </ul>
          </UiCard>
        </li>
      </ul>
      <UiButton
        v-if="more"
        variant="secondary"
        block
        :disabled="loadingMore"
        @click="loadMore"
      >
        {{ $t('audit.loadMore') }}
      </UiButton>
    </template>
  </div>
</template>

<style scoped>
.stack { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.intro { margin: 0; color: var(--muted); font-size: var(--font-size-sm); }
.entries, .changes { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--space-3); }
.entry p { margin: 0; }
.headline { display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); }
.meta { margin-top: var(--space-1) !important; color: var(--muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
.changes { margin-top: var(--space-3); gap: var(--space-2); }
.changes li { display: grid; gap: 2px; font-size: var(--font-size-sm); }
.field { color: var(--muted); }
.values { overflow-wrap: anywhere; }
</style>
