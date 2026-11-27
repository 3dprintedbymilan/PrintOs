<script setup lang="ts">
import {
  acceptedModelAttribute,
  appendProfile,
  emptyProfileForm,
  formatFileSize,
  formatTags,
  hasProfile,
  maxModelFileBytes,
  parseTags,
  profileToForm,
  validateModelFile,
  validateProfileForm,
} from '~/utils/modelLibrary'
import type { DownloadLink, ModelDetail, ModelVersion } from '~/utils/modelLibrary'

definePageMeta({ titleKey: 'models.detailTitle' })

const { t, locale } = useI18n()
const route = useRoute()
const auth = useAuthStore()
const apiBase = useRuntimeConfig().public.apiBase
const id = String(route.params.id)

const model = ref<ModelDetail | null>(null)
const status = ref<'loading' | 'ready' | 'failed' | 'missing'>('loading')
const selectedVersionId = ref('')
const message = ref('')
const actionError = ref('')

const editing = ref(false)
const editName = ref('')
const editDescription = ref('')
const editTags = ref('')
const editSourceUrl = ref('')
const editErrors = ref<Record<string, string>>({})
const saving = ref(false)

const editingProfile = ref(false)
const profileForm = ref(emptyProfileForm())
const profileNotes = ref('')
const profileErrors = ref<Record<string, string>>({})

const addingVersion = ref(false)
const versionFile = ref<File | null>(null)
const versionNotes = ref('')
const versionProfile = ref(emptyProfileForm())
const versionErrors = ref<Record<string, string>>({})
const versionFileId = useId()

const confirmDelete = ref(false)
const deleting = ref(false)

const selected = computed<ModelVersion | null>(() => model.value?.versions.find(version => version.id === selectedVersionId.value) ?? model.value?.versions[0] ?? null)
const latestId = computed(() => model.value?.versions[0]?.id)
const versionOptions = computed(() => (model.value?.versions ?? []).map(version => ({
  value: version.id,
  label: t('models.versionLabel', { number: version.versionNumber, name: version.fileName }),
})))
const wantThumbnail = computed(() => !!model.value && !model.value.thumbnailUrl && selected.value?.id === latestId.value)

async function load() {
  status.value = 'loading'
  try {
    model.value = await auth.authorizedFetch<ModelDetail>(`/api/models/${id}`)
    selectedVersionId.value = model.value.versions[0]?.id ?? ''
    status.value = 'ready'
  }
  catch (error) {
    status.value = parseApiError(error).status === 404 ? 'missing' : 'failed'
  }
}

function describeError(error: unknown) {
  const parsed = parseApiError(error)
  if (parsed.kind === 'validation') return Object.values(parsed.fieldErrors)[0]?.[0] ?? t('models.errors.save')
  if (parsed.status === 413) return t('models.errors.size', { max: formatFileSize(maxModelFileBytes) })
  if (parsed.status === 503) return t('models.errors.storage')
  return parsed.kind === 'network' ? t('models.errors.network') : t('models.errors.save')
}

function focusInvalid() {
  nextTick(() => document.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus())
}

function startEdit() {
  if (!model.value) return
  editName.value = model.value.name
  editDescription.value = model.value.description ?? ''
  editTags.value = formatTags(model.value.tags)
  editSourceUrl.value = model.value.sourceUrl ?? ''
  editErrors.value = {}
  editing.value = true
}

async function saveEdit() {
  actionError.value = ''
  if (!editName.value.trim() || editName.value.trim().length > 100) {
    editErrors.value = { name: t('models.errors.nameRequired') }
    focusInvalid()
    return
  }
  saving.value = true
  try {
    model.value = await auth.authorizedFetch<ModelDetail>(`/api/models/${id}`, {
      method: 'PUT',
      body: { name: editName.value.trim(), description: editDescription.value.trim() || null, tags: parseTags(editTags.value), sourceUrl: editSourceUrl.value.trim() || null },
    })
    editing.value = false
    message.value = t('models.saved')
  }
  catch (error) {
    const parsed = parseApiError(error)
    if (parsed.kind === 'validation' && parsed.fieldErrors.sourceUrl) {
      editErrors.value = { sourceUrl: t('models.errors.sourceUrl') }
      focusInvalid()
    }
    else if (parsed.kind === 'validation' && parsed.fieldErrors.tags) {
      editErrors.value = { tags: t('models.errors.tags') }
      focusInvalid()
    }
    else actionError.value = describeError(error)
  }
  finally {
    saving.value = false
  }
}

function startProfileEdit() {
  if (!selected.value) return
  profileForm.value = profileToForm(selected.value.printProfile)
  profileNotes.value = selected.value.notes ?? ''
  profileErrors.value = {}
  editingProfile.value = true
}

function profileErrorMap(errors: ReturnType<typeof validateProfileForm>['errors']) {
  const next: Record<string, string> = {}
  if (errors.layerHeight) next.layerHeight = t(`models.errors.${errors.layerHeight === 'range' ? 'layerRange' : 'number'}`)
  if (errors.infill) next.infill = t(`models.errors.${errors.infill === 'range' ? 'infillRange' : 'number'}`)
  return next
}

async function saveProfile() {
  if (!selected.value) return
  actionError.value = ''
  const { profile, errors } = validateProfileForm(profileForm.value)
  profileErrors.value = profileErrorMap(errors)
  if (!profile) {
    focusInvalid()
    return
  }
  saving.value = true
  try {
    await auth.authorizedFetch(`/api/models/${id}/versions/${selected.value.id}`, {
      method: 'PUT',
      body: { notes: profileNotes.value.trim() || null, ...profile },
    })
    const keep = selected.value.id
    model.value = await auth.authorizedFetch<ModelDetail>(`/api/models/${id}`)
    selectedVersionId.value = keep
    editingProfile.value = false
    message.value = t('models.saved')
  }
  catch (error) {
    actionError.value = describeError(error)
  }
  finally {
    saving.value = false
  }
}

function onVersionFile(event: Event) {
  versionFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
  versionErrors.value = { ...versionErrors.value, file: '' }
}

async function addVersion() {
  actionError.value = ''
  const next: Record<string, string> = {}
  if (!versionFile.value) next.file = t('models.errors.required')
  else {
    const kind = validateModelFile(versionFile.value)
    if (kind) next.file = kind === 'size' ? t('models.errors.size', { max: formatFileSize(maxModelFileBytes) }) : t(`models.errors.${kind}`)
  }
  const { profile, errors } = validateProfileForm(versionProfile.value)
  Object.assign(next, profileErrorMap(errors))
  versionErrors.value = next
  if (Object.keys(next).length > 0 || !versionFile.value || !profile) {
    focusInvalid()
    return
  }

  const body = new FormData()
  body.append('file', versionFile.value)
  if (versionNotes.value.trim()) body.append('notes', versionNotes.value.trim())
  appendProfile(body, profile)

  saving.value = true
  try {
    model.value = await auth.authorizedFetch<ModelDetail>(`/api/models/${id}/versions`, { method: 'POST', body })
    selectedVersionId.value = model.value.versions[0]?.id ?? ''
    addingVersion.value = false
    versionFile.value = null
    versionNotes.value = ''
    versionProfile.value = emptyProfileForm()
    message.value = t('models.versionAdded')
  }
  catch (error) {
    actionError.value = describeError(error)
  }
  finally {
    saving.value = false
  }
}

async function download() {
  if (!selected.value) return
  actionError.value = ''
  try {
    const link = await auth.authorizedFetch<DownloadLink>(`/api/models/${id}/versions/${selected.value.id}/download-url`)
    const anchor = document.createElement('a')
    anchor.href = `${apiBase}${link.url}`
    anchor.download = selected.value.fileName
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  }
  catch {
    actionError.value = t('models.errors.download')
  }
}

async function removeModel() {
  deleting.value = true
  actionError.value = ''
  try {
    await auth.authorizedFetch(`/api/models/${id}`, { method: 'DELETE' })
    await navigateTo('/prints?section=models')
  }
  catch {
    confirmDelete.value = false
    actionError.value = t('models.errors.delete')
  }
  finally {
    deleting.value = false
  }
}

async function saveThumbnail(blob: Blob) {
  try {
    const body = new FormData()
    body.append('thumbnail', blob, 'thumbnail.png')
    model.value = await auth.authorizedFetch<ModelDetail>(`/api/models/${id}/thumbnail`, { method: 'PUT', body })
  }
  catch {
    // the preview image is generated again next time
  }
}

onMounted(load)
</script>

<template>
  <div class="stack">
    <NuxtLink
      to="/prints?section=models"
      class="back"
    >
      {{ $t('models.back') }}
    </NuxtLink>

    <StateLoading v-if="status === 'loading'" />
    <StateEmpty
      v-else-if="status === 'missing'"
      :message="$t('models.notFound')"
    />
    <StateError
      v-else-if="status === 'failed'"
      @retry="load"
    />

    <template v-else-if="model && selected">
      <UiSelect
        v-if="model.versions.length > 1"
        v-model="selectedVersionId"
        :label="$t('models.version')"
        :options="versionOptions"
      />
      <ModelViewer
        :key="selected.id"
        :model-id="model.id"
        :version="selected"
        :want-thumbnail="wantThumbnail"
        @thumbnail="saveThumbnail"
      />

      <p
        v-if="message"
        class="notice"
        role="status"
      >
        {{ message }}
      </p>
      <p
        v-if="actionError"
        class="error"
        role="alert"
      >
        {{ actionError }}
      </p>

      <UiCard
        v-if="!editing"
        class="group"
      >
        <h2 class="name">
          {{ model.name }}
        </h2>
        <p
          v-if="model.description"
          class="text"
        >
          {{ model.description }}
        </p>
        <ul
          v-if="model.tags.length > 0"
          class="chips"
          :aria-label="$t('models.fields.tags')"
        >
          <li
            v-for="tag in model.tags"
            :key="tag"
          >
            {{ tag }}
          </li>
        </ul>
        <a
          v-if="model.sourceUrl"
          :href="model.sourceUrl"
          class="source"
          target="_blank"
          rel="noopener noreferrer"
        >{{ model.sourceUrl }}</a>
        <UiButton
          variant="secondary"
          @click="startEdit"
        >
          {{ $t('models.edit') }}
        </UiButton>
      </UiCard>

      <form
        v-else
        class="form"
        novalidate
        @submit.prevent="saveEdit"
      >
        <UiCard class="group">
          <UiInput
            v-model="editName"
            :label="$t('models.fields.name')"
            :error="editErrors.name"
          />
          <UiInput
            v-model="editDescription"
            :label="$t('models.fields.description')"
          />
          <UiInput
            v-model="editTags"
            :label="$t('models.fields.tags')"
            :hint="editErrors.tags ? undefined : $t('models.hints.tags')"
            :error="editErrors.tags"
          />
          <UiInput
            v-model="editSourceUrl"
            :label="$t('models.fields.sourceUrl')"
            type="url"
            :error="editErrors.sourceUrl"
          />
          <div class="actions">
            <UiButton
              type="submit"
              :loading="saving"
            >
              {{ $t('models.save') }}
            </UiButton>
            <UiButton
              variant="ghost"
              @click="editing = false"
            >
              {{ $t('models.cancel') }}
            </UiButton>
          </div>
        </UiCard>
      </form>

      <ModelLicenseCard
        :model-id="model.id"
        :license="model.license"
        @updated="model = $event"
      />

      <UiCard class="group">
        <h2 class="heading">
          {{ $t('models.profile.title') }}
        </h2>
        <p class="meta">
          {{ selected.fileName }} · {{ selected.format.toUpperCase() }} · {{ formatFileSize(selected.sizeBytes) }} · {{ new Date(selected.createdAt).toLocaleDateString(locale) }}
        </p>
        <p
          v-if="selected.notes && !editingProfile"
          class="text"
        >
          {{ selected.notes }}
        </p>

        <form
          v-if="editingProfile"
          class="form"
          novalidate
          @submit.prevent="saveProfile"
        >
          <ModelProfileFields
            v-model="profileForm"
            :errors="profileErrors"
          />
          <UiInput
            v-model="profileNotes"
            :label="$t('models.fields.versionNotes')"
          />
          <div class="actions">
            <UiButton
              type="submit"
              :loading="saving"
            >
              {{ $t('models.save') }}
            </UiButton>
            <UiButton
              variant="ghost"
              @click="editingProfile = false"
            >
              {{ $t('models.cancel') }}
            </UiButton>
          </div>
        </form>
        <template v-else>
          <dl
            v-if="hasProfile(selected.printProfile)"
            class="profile"
          >
            <template v-if="selected.printProfile.layerHeightMillimetres !== null">
              <dt>{{ $t('models.profile.layerHeight') }}</dt>
              <dd>{{ $n(selected.printProfile.layerHeightMillimetres) }} mm</dd>
            </template>
            <template v-if="selected.printProfile.infillPercent !== null">
              <dt>{{ $t('models.profile.infill') }}</dt>
              <dd>{{ selected.printProfile.infillPercent }}%</dd>
            </template>
            <template v-if="selected.printProfile.supportsRequired !== null">
              <dt>{{ $t('models.profile.supports') }}</dt>
              <dd>{{ selected.printProfile.supportsRequired ? $t('models.profile.supportsYes') : $t('models.profile.supportsNo') }}</dd>
            </template>
            <template v-if="selected.printProfile.materialType">
              <dt>{{ $t('models.profile.material') }}</dt>
              <dd>{{ selected.printProfile.materialType }}</dd>
            </template>
            <template v-if="selected.printProfile.printProfileNotes">
              <dt>{{ $t('models.profile.notes') }}</dt>
              <dd>{{ selected.printProfile.printProfileNotes }}</dd>
            </template>
          </dl>
          <p
            v-else
            class="meta"
          >
            {{ $t('models.profile.none') }}
          </p>
          <UiButton
            variant="secondary"
            @click="startProfileEdit"
          >
            {{ $t('models.profile.edit') }}
          </UiButton>
        </template>
      </UiCard>

      <UiButton
        block
        variant="secondary"
        @click="download"
      >
        {{ $t('models.download') }}
      </UiButton>

      <UiButton
        v-if="!addingVersion"
        block
        variant="secondary"
        @click="addingVersion = true"
      >
        {{ $t('models.addVersion') }}
      </UiButton>
      <form
        v-else
        class="form"
        novalidate
        @submit.prevent="addVersion"
      >
        <UiCard class="group">
          <h2 class="heading">
            {{ $t('models.addVersion') }}
          </h2>
          <div class="field">
            <label :for="versionFileId">{{ $t('models.fields.file') }}</label>
            <input
              :id="versionFileId"
              type="file"
              :accept="acceptedModelAttribute"
              :aria-invalid="versionErrors.file ? 'true' : undefined"
              :aria-describedby="`${versionFileId}-help`"
              @change="onVersionFile"
            >
            <p
              :id="`${versionFileId}-help`"
              class="help"
              :class="{ error: !!versionErrors.file }"
            >
              {{ versionErrors.file || $t('models.hints.file', { max: formatFileSize(maxModelFileBytes) }) }}
            </p>
          </div>
          <UiInput
            v-model="versionNotes"
            :label="$t('models.fields.versionNotes')"
          />
          <ModelProfileFields
            v-model="versionProfile"
            :errors="versionErrors"
          />
          <div class="actions">
            <UiButton
              type="submit"
              :loading="saving"
            >
              {{ saving ? $t('models.uploading') : $t('models.uploadSubmit') }}
            </UiButton>
            <UiButton
              variant="ghost"
              @click="addingVersion = false"
            >
              {{ $t('models.cancel') }}
            </UiButton>
          </div>
        </UiCard>
      </form>

      <UiButton
        block
        variant="danger"
        @click="confirmDelete = true"
      >
        {{ $t('models.delete') }}
      </UiButton>

      <UiDialog
        v-model="confirmDelete"
        :title="$t('models.deleteTitle')"
      >
        <p class="text">
          {{ $t('models.deleteBody', { name: model.name }) }}
        </p>
        <div class="actions">
          <UiButton
            variant="danger"
            :loading="deleting"
            @click="removeModel"
          >
            {{ $t('models.deleteConfirm') }}
          </UiButton>
          <UiButton
            variant="ghost"
            @click="confirmDelete = false"
          >
            {{ $t('models.cancel') }}
          </UiButton>
        </div>
      </UiDialog>
    </template>
  </div>
</template>

<style scoped>
.stack, .form { display: grid; gap: var(--space-3); }
.back { display: inline-flex; align-items: center; justify-self: start; min-height: var(--tap-size); color: var(--accent); font-weight: 600; text-decoration: none; }
.group { display: grid; gap: var(--space-3); }
.name { margin: 0; font-size: var(--font-size-lg, 1.25rem); overflow-wrap: anywhere; }
.heading { margin: 0; font-size: var(--font-size-md, 1rem); }
.text { margin: 0; overflow-wrap: anywhere; white-space: pre-line; }
.meta { margin: 0; font-size: var(--font-size-sm); color: var(--muted); overflow-wrap: anywhere; }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: 0; padding: 0; list-style: none; }
.chips li { padding: var(--space-1) var(--space-3); background: var(--accent-soft); color: var(--accent-text); border-radius: var(--radius-pill); font-size: var(--font-size-sm); }
.source { display: inline-flex; align-items: center; min-height: var(--tap-size); color: var(--accent-text); overflow-wrap: anywhere; }
.profile { display: grid; grid-template-columns: auto 1fr; gap: var(--space-2) var(--space-4); margin: 0; }
.profile dt { color: var(--muted); font-size: var(--font-size-sm); }
.profile dd { margin: 0; overflow-wrap: anywhere; }
.actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.notice { margin: 0; color: var(--accent-text); font-size: var(--font-size-sm); }
.error { margin: 0; color: var(--danger); font-size: var(--font-size-sm); }
.field { display: grid; gap: var(--space-1); }
label { font-size: var(--font-size-sm); color: var(--muted); }
input[type='file'] { min-height: var(--tap-size); padding: var(--space-3); color: var(--text); background: var(--surface-2); border: 1px dashed var(--border); border-radius: var(--radius-md); font: inherit; }
input[type='file'][aria-invalid='true'] { border-color: var(--danger); }
.help { margin: 0; font-size: var(--font-size-sm); color: var(--muted); }
.help.error { color: var(--danger); }
</style>
