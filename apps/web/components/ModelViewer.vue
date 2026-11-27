<script setup lang="ts">
import { formatFileSize, maxPreviewBytes } from '~/utils/modelLibrary'
import type { DownloadLink, ModelVersion } from '~/utils/modelLibrary'
import type { ModelViewerHandle } from '~/utils/modelViewer'

const props = defineProps<{ modelId: string, version: ModelVersion, wantThumbnail?: boolean }>()
const emit = defineEmits<{ thumbnail: [blob: Blob] }>()

const auth = useAuthStore()
const apiBase = useRuntimeConfig().public.apiBase
const host = ref<HTMLElement>()
const state = ref<'loading' | 'ready' | 'tooLarge' | 'unsupported' | 'failed'>('loading')
const progress = ref(0)
let handle: ModelViewerHandle | null = null
let run = 0

async function download(url: string, token: number): Promise<ArrayBuffer | null> {
  const response = await fetch(`${apiBase}${url}`)
  if (!response.ok) throw new Error(`download failed with ${response.status}`)
  const total = Number(response.headers.get('content-length')) || props.version.sizeBytes
  if (!response.body) return await response.arrayBuffer()

  const reader = response.body.getReader()
  const chunks: Uint8Array[] = []
  let received = 0
  for (;;) {
    const { done, value } = await reader.read()
    if (token !== run) {
      await reader.cancel()
      return null
    }
    if (done) break
    chunks.push(value)
    received += value.length
    progress.value = total > 0 ? Math.min(99, Math.round((received / total) * 100)) : 0
  }
  const merged = new Uint8Array(received)
  let offset = 0
  for (const chunk of chunks) {
    merged.set(chunk, offset)
    offset += chunk.length
  }
  return merged.buffer
}

async function start() {
  const token = ++run
  handle?.dispose()
  handle = null
  progress.value = 0

  if (props.version.sizeBytes > maxPreviewBytes) {
    state.value = 'tooLarge'
    return
  }

  state.value = 'loading'
  try {
    const link = await auth.authorizedFetch<DownloadLink>(`/api/models/${props.modelId}/versions/${props.version.id}/download-url`)
    const buffer = await download(link.url, token)
    if (!buffer || token !== run) return
    const { mountModelViewer, ViewerUnsupportedError } = await import('~/utils/modelViewer')
    await nextTick()
    if (!host.value) return
    try {
      handle = await mountModelViewer(host.value, buffer, props.version.format)
    }
    catch (error) {
      state.value = error instanceof ViewerUnsupportedError ? 'unsupported' : 'failed'
      return
    }
    if (token !== run) {
      handle.dispose()
      handle = null
      return
    }
    state.value = 'ready'
    if (props.wantThumbnail) {
      const blob = await handle.capture()
      if (blob) emit('thumbnail', blob)
    }
  }
  catch {
    if (token === run) state.value = 'failed'
  }
}

watch(() => props.version.id, start)
onMounted(start)
onBeforeUnmount(() => {
  run++
  handle?.dispose()
  handle = null
})
</script>

<template>
  <div>
  <div class="viewer">
    <div
      ref="host"
      class="canvas"
      :class="{ hidden: state !== 'ready' }"
      role="img"
      :aria-label="$t('models.viewer.label', { name: version.fileName })"
    />
    <div
      v-if="state === 'loading'"
      class="overlay"
      role="status"
    >
      <span class="spinner" />
      <span>{{ progress > 0 ? $t('models.viewer.loadingPercent', { percent: progress }) : $t('models.viewer.loading') }}</span>
    </div>
    <div
      v-else-if="state !== 'ready'"
      class="overlay"
      role="alert"
    >
      <span>{{ state === 'tooLarge' ? $t('models.viewer.tooLarge', { size: formatFileSize(version.sizeBytes) }) : $t(`models.viewer.${state}`) }}</span>
      <button
        v-if="state === 'failed'"
        type="button"
        class="retry"
        @click="start"
      >
        {{ $t('state.retry') }}
      </button>
    </div>
  </div>
    <p
      v-if="state === 'ready'"
      class="hint"
    >
      {{ $t('models.viewer.hint') }}
    </p>
  </div>
</template>

<style scoped>
.viewer { position: relative; aspect-ratio: 4 / 3; overflow: hidden; background: radial-gradient(circle at 50% 40%, var(--surface-2), var(--surface)); border: 1px solid var(--border); border-radius: var(--radius-lg, var(--radius-md)); }
.canvas { position: absolute; inset: 0; }
.canvas.hidden { visibility: hidden; }
.overlay { position: absolute; inset: 0; display: grid; place-content: center; justify-items: center; gap: var(--space-3); padding: var(--space-4); text-align: center; color: var(--muted); font-size: var(--font-size-sm); }
.spinner { width: 24px; height: 24px; border: 3px solid var(--accent); border-right-color: transparent; border-radius: 50%; animation: spin 0.8s linear infinite; }
.retry { min-height: var(--tap-size); padding: 0 var(--space-5); background: var(--accent); color: var(--accent-contrast); border: 0; border-radius: var(--radius-pill); font: inherit; font-weight: 600; cursor: pointer; }
.hint { margin: var(--space-2) 0 0; text-align: center; font-size: var(--font-size-sm); color: var(--muted); }
@keyframes spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2.4s; } }
</style>
