<script setup lang="ts">
import {
  acceptedModelAttribute,
  appendProfile,
  emptyProfileForm,
  formatFileSize,
  maxModelFileBytes,
  nameFromFileName,
  parseTags,
  validateModelFile,
  validateProfileForm,
} from '~/utils/modelLibrary'
import type { ModelDetail } from '~/utils/modelLibrary'

definePageMeta({ titleKey: 'models.uploadTitle' })

const { t } = useI18n()
const auth = useAuthStore()

const fileInput = ref<HTMLInputElement>()
const file = ref<File | null>(null)
const name = ref('')
const description = ref('')
const tags = ref('')
const sourceUrl = ref('')
const notes = ref('')
const profile = ref(emptyProfileForm())
const errors = ref<Record<string, string>>({})
const saving = ref(false)
const saveError = ref('')
let autoName = ''
const fileId = useId()
const descriptionId = useId()

function onFile(event: Event) {
  const picked = (event.target as HTMLInputElement).files?.[0] ?? null
  file.value = picked
  errors.value = { ...errors.value, file: '' }
  if (picked && (!name.value.trim() || name.value === autoName)) {
    autoName = nameFromFileName(picked.name)
    name.value = autoName
  }
}

function fileErrorText(kind: ReturnType<typeof validateModelFile>) {
  return kind === 'size' ? t('models.errors.size', { max: formatFileSize(maxModelFileBytes) }) : t(`models.errors.${kind ?? 'required'}`)
}

async function save() {
  saveError.value = ''
  const next: Record<string, string> = {}
  if (!file.value) next.file = t('models.errors.required')
  else {
    const kind = validateModelFile(file.value)
    if (kind) next.file = fileErrorText(kind)
  }
  if (name.value.length > 100) next.name = t('models.errors.nameLength')
  const { profile: validated, errors: profileErrors } = validateProfileForm(profile.value)
  if (profileErrors.layerHeight) next.layerHeight = t(`models.errors.${profileErrors.layerHeight === 'range' ? 'layerRange' : 'number'}`)
  if (profileErrors.infill) next.infill = t(`models.errors.${profileErrors.infill === 'range' ? 'infillRange' : 'number'}`)

  errors.value = next
  if (Object.keys(next).length > 0 || !file.value || !validated) {
    await nextTick()
    document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  const body = new FormData()
  body.append('file', file.value)
  if (name.value.trim()) body.append('name', name.value.trim())
  if (description.value.trim()) body.append('description', description.value.trim())
  if (tags.value.trim()) body.append('tags', parseTags(tags.value).join(','))
  if (sourceUrl.value.trim()) body.append('sourceUrl', sourceUrl.value.trim())
  if (notes.value.trim()) body.append('notes', notes.value.trim())
  appendProfile(body, validated)

  saving.value = true
  try {
    const created = await auth.authorizedFetch<ModelDetail>('/api/models', { method: 'POST', body })
    await navigateTo(`/models/${created.id}`)
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation') {
      const server: Record<string, string> = {}
      for (const [field, messages] of Object.entries(parsed.fieldErrors)) server[field] = messages[0] ?? ''
      if (server.layerHeightMillimetres) server.layerHeight = t('models.errors.layerRange')
      if (server.infillPercent) server.infill = t('models.errors.infillRange')
      if (server.sourceUrl) server.sourceUrl = t('models.errors.sourceUrl')
      if (server.tags) server.tags = t('models.errors.tags')
      errors.value = server
      if (Object.keys(server).length === 0) saveError.value = t('models.errors.save')
      await nextTick()
      document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    }
    else if (parsed.status === 413) saveError.value = t('models.errors.size', { max: formatFileSize(maxModelFileBytes) })
    else if (parsed.status === 503) saveError.value = t('models.errors.storage')
    else saveError.value = parsed.kind === 'network' ? t('models.errors.network') : t('models.errors.save')
  }
  finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/prints?section=models"
      class="back"
    >
      {{ $t('models.back') }}
    </NuxtLink>

    <form
      class="form"
      novalidate
      @submit.prevent="save"
    >
      <UiCard class="group">
        <div class="field">
          <label :for="fileId">{{ $t('models.fields.file') }}</label>
          <input
            :id="fileId"
            ref="fileInput"
            type="file"
            :accept="acceptedModelAttribute"
            :aria-invalid="errors.file ? 'true' : undefined"
            :aria-describedby="`${fileId}-help`"
            @change="onFile"
          >
          <p
            :id="`${fileId}-help`"
            class="help"
            :class="{ error: !!errors.file }"
          >
            {{ errors.file || (file ? `${file.name} · ${formatFileSize(file.size)}` : $t('models.hints.file', { max: formatFileSize(maxModelFileBytes) })) }}
          </p>
        </div>
        <UiInput
          v-model="name"
          :label="$t('models.fields.name')"
          :error="errors.name"
        />
        <div class="field">
          <label :for="descriptionId">{{ $t('models.fields.description') }}</label>
          <textarea
            :id="descriptionId"
            v-model="description"
            rows="3"
            maxlength="2000"
          />
        </div>
        <UiInput
          v-model="tags"
          :label="$t('models.fields.tags')"
          :hint="errors.tags ? undefined : $t('models.hints.tags')"
          :error="errors.tags"
        />
        <UiInput
          v-model="sourceUrl"
          :label="$t('models.fields.sourceUrl')"
          type="url"
          :error="errors.sourceUrl ? $t('models.errors.sourceUrl') : undefined"
        />
      </UiCard>

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('models.profile.title') }}
        </h2>
        <ModelProfileFields
          v-model="profile"
          :errors="errors"
        />
        <UiInput
          v-model="notes"
          :label="$t('models.fields.versionNotes')"
        />
      </UiCard>

      <p
        v-if="saveError"
        class="error"
        role="alert"
      >
        {{ saveError }}
      </p>

      <UiButton
        type="submit"
        block
        :loading="saving"
      >
        {{ saving ? $t('models.uploading') : $t('models.uploadSubmit') }}
      </UiButton>
      <p
        v-if="saving"
        class="help"
        role="status"
      >
        {{ $t('models.uploadingHint') }}
      </p>
    </form>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.heading { margin: 0; font-size: var(--font-size-md, 1rem); }
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
input[type='file'] { min-height: var(--tap-size); padding: var(--space-3); color: var(--text); background: var(--surface-2); border: 1px dashed var(--border); border-radius: var(--radius-md); font: inherit; }
input[type='file'][aria-invalid='true'] { border-color: var(--danger); }
textarea { width: 100%; padding: var(--space-3) var(--space-4); background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius-md); font: inherit; resize: vertical; }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); overflow-wrap: anywhere; }
.help.error, .error { color: var(--danger); }
.error { margin: 0; font-size: var(--font-size-sm); }
</style>
