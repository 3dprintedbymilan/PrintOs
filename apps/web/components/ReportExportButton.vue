<script setup lang="ts">
const props = defineProps<{
  path: string
  fileName: string
  label: string
}>()

const auth = useAuthStore()
const state = ref<'idle' | 'running' | 'done' | 'failed'>('idle')

async function download() {
  state.value = 'running'
  try {
    const blob = await auth.authorizedFetch<Blob>(props.path, { responseType: 'blob' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = props.fileName
    document.body.append(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 10000)
    state.value = 'done'
  }
  catch {
    state.value = 'failed'
  }
}
</script>

<template>
  <div class="export">
    <UiButton
      variant="secondary"
      block
      :disabled="state === 'running'"
      @click="download"
    >
      {{ state === 'running' ? $t('reports.export.running') : label }}
    </UiButton>
    <p
      v-if="state === 'done'"
      role="status"
      class="done"
    >
      {{ $t('reports.export.done', { name: fileName }) }}
    </p>
    <p
      v-else-if="state === 'failed'"
      role="alert"
      class="failed"
    >
      {{ $t('reports.export.failed') }}
    </p>
  </div>
</template>

<style scoped>
.export { display: grid; gap: var(--space-1); }
p { margin: 0; font-size: var(--font-size-sm); }
.done { color: var(--success); }
.failed { color: var(--danger); }
</style>
