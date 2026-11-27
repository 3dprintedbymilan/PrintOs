<script setup lang="ts">
import { formatFileSize } from '~/utils/modelLibrary'
import type { ModelSummary } from '~/utils/modelLibrary'

const { t, locale } = useI18n()
const auth = useAuthStore()
const apiBase = useRuntimeConfig().public.apiBase

const models = ref<ModelSummary[]>([])
const tags = ref<string[]>([])
const status = ref<'loading' | 'ready' | 'failed'>('loading')
const search = ref('')
const tag = ref('')
const view = ref<'grid' | 'list'>('grid')
const everLoaded = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined
let requestId = 0

const viewOptions = computed(() => [
  { value: 'grid', label: t('models.viewGrid') },
  { value: 'list', label: t('models.viewList') },
])
const filtering = computed(() => search.value.trim() !== '' || tag.value !== '')

async function load() {
  const current = ++requestId
  status.value = 'loading'
  try {
    const query: Record<string, string> = {}
    if (search.value.trim()) query.search = search.value.trim()
    if (tag.value) query.tag = tag.value
    const [loadedModels, loadedTags] = await Promise.all([
      auth.authorizedFetch<ModelSummary[]>('/api/models', { query }),
      auth.authorizedFetch<string[]>('/api/models/tags'),
    ])
    if (current !== requestId) return
    models.value = loadedModels
    tags.value = loadedTags
    everLoaded.value = true
    status.value = 'ready'
  }
  catch {
    if (current === requestId) status.value = 'failed'
  }
}

function toggleTag(value: string) {
  tag.value = tag.value === value ? '' : value
}

function thumbnailSource(model: ModelSummary) {
  return model.thumbnailUrl ? `${apiBase}${model.thumbnailUrl}` : null
}

function details(model: ModelSummary) {
  const parts = [model.format?.toUpperCase(), model.sizeBytes === null ? null : formatFileSize(model.sizeBytes, locale.value)]
  if (model.versionCount > 1) parts.push(t('models.versions', { count: model.versionCount }, model.versionCount))
  return parts.filter(Boolean).join(' · ')
}

watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 250)
})
watch(tag, load)
watch(view, (value) => {
  try {
    localStorage.setItem('printos_models_view', value)
  }
  catch {
    // the view choice is only a convenience
  }
})

onMounted(() => {
  try {
    const saved = localStorage.getItem('printos_models_view')
    if (saved === 'grid' || saved === 'list') view.value = saved
  }
  catch {
    // storage can be blocked
  }
  load()
})
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <div class="library">
    <UiButton
      block
      @click="navigateTo('/models/new')"
    >
      {{ $t('models.upload') }}
    </UiButton>

    <template v-if="everLoaded && (models.length > 0 || filtering || tags.length > 0)">
      <UiInput
        v-model="search"
        :label="$t('models.search')"
        type="search"
      />
      <div
        v-if="tags.length > 0"
        class="tags"
        role="group"
        :aria-label="$t('models.filterByTag')"
      >
        <button
          v-for="item in tags"
          :key="item"
          type="button"
          class="tag"
          :aria-pressed="tag === item"
          @click="toggleTag(item)"
        >
          {{ item }}
        </button>
      </div>
      <UiSegmentedControl
        v-model="view"
        :label="$t('models.view')"
        :options="viewOptions"
      />
    </template>

    <StateLoading v-if="status === 'loading' && !everLoaded" />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />
    <template v-else>
      <StateEmpty
        v-if="models.length === 0"
        :message="filtering ? $t('models.noMatches') : $t('models.empty')"
      />
      <ul
        v-else
        class="items"
        :class="view"
        :aria-busy="status === 'loading'"
      >
        <li
          v-for="model in models"
          :key="model.id"
        >
          <NuxtLink
            :to="`/models/${model.id}`"
            class="item"
          >
            <span class="thumb">
              <img
                v-if="thumbnailSource(model)"
                :src="thumbnailSource(model)!"
                alt=""
                loading="lazy"
              >
              <svg
                v-else
                viewBox="0 0 24 24"
                width="28"
                height="28"
                fill="none"
                stroke="currentColor"
                stroke-width="1.6"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z" />
                <path d="M4 7.5L12 12l8-4.5M12 12v9" />
              </svg>
            </span>
            <span class="text">
              <span class="name">{{ model.name }}</span>
              <span class="meta">{{ details(model) }}</span>
              <span
                v-if="model.tags.length > 0"
                class="meta tagline"
              >{{ model.tags.join(' · ') }}</span>
            </span>
          </NuxtLink>
        </li>
      </ul>
    </template>
  </div>
</template>

<style scoped>
.library { display: grid; gap: var(--space-3); }
.tags { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.tag { min-height: var(--tap-size); padding: 0 var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-pill); font: inherit; font-size: var(--font-size-sm); cursor: pointer; }
.tag[aria-pressed='true'] { background: var(--accent-soft); border-color: var(--accent); color: var(--accent-text); }
.items { display: grid; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.items.grid { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-3); }
.item { display: flex; gap: var(--space-3); align-items: center; min-height: 64px; padding: var(--space-3); color: var(--text); text-decoration: none; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-md); }
.grid .item { flex-direction: column; align-items: stretch; height: 100%; }
.thumb { display: grid; place-items: center; flex: none; width: 56px; height: 56px; overflow: hidden; color: var(--muted); background: var(--surface-2); border-radius: var(--radius-md); }
.grid .thumb { width: 100%; height: auto; aspect-ratio: 1; }
.thumb img { width: 100%; height: 100%; object-fit: cover; }
.text { display: grid; min-width: 0; gap: 2px; }
.name { font-weight: 600; overflow-wrap: anywhere; }
.meta { font-size: var(--font-size-sm); color: var(--muted); }
.tagline { overflow-wrap: anywhere; }
</style>
