import { parseDecimal } from './settingsForm'

export interface PrintProfile {
  layerHeightMillimetres: number | null
  infillPercent: number | null
  supportsRequired: boolean | null
  materialType: string | null
  printProfileNotes: string | null
}

export interface ModelVersion {
  id: string
  versionNumber: number
  fileName: string
  format: 'stl' | '3mf' | 'obj'
  sizeBytes: number
  notes: string | null
  printProfile: PrintProfile
  createdAt: string
}

export interface ModelSummary {
  id: string
  name: string
  description: string | null
  tags: string[]
  format: string | null
  sizeBytes: number | null
  versionCount: number
  thumbnailUrl: string | null
  createdAt: string
  updatedAt: string
}

export interface ModelDetail {
  id: string
  name: string
  description: string | null
  sourceUrl: string | null
  tags: string[]
  thumbnailUrl: string | null
  versions: ModelVersion[]
  createdAt: string
  updatedAt: string
}

export interface DownloadLink {
  url: string
  expiresAt: string
}

export const maxModelFileBytes = 100 * 1024 * 1024
export const maxPreviewBytes = 60 * 1024 * 1024
export const acceptedModelExtensions = ['stl', '3mf', 'obj']
export const acceptedModelAttribute = acceptedModelExtensions.map(extension => `.${extension}`).join(',')

export type FileErrorKind = 'type' | 'empty' | 'size'

export function fileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return dot < 0 ? '' : fileName.slice(dot + 1).toLowerCase()
}

export function validateModelFile(file: { name: string, size: number }): FileErrorKind | null {
  if (!acceptedModelExtensions.includes(fileExtension(file.name))) return 'type'
  if (file.size === 0) return 'empty'
  if (file.size > maxModelFileBytes) return 'size'
  return null
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function nameFromFileName(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return (dot > 0 ? fileName.slice(0, dot) : fileName).trim().slice(0, 100)
}

export function parseTags(raw: string): string[] {
  return [...new Set(raw.split(/[,;]/).map(tag => tag.trim().toLowerCase()).filter(Boolean))]
}

export function formatTags(tags: string[]): string {
  return tags.join(', ')
}

export interface ProfileFormValues {
  layerHeight: string
  infill: string
  supports: '' | 'yes' | 'no'
  materialType: string
  profileNotes: string
}

export interface ValidatedProfile {
  layerHeightMillimetres: number | null
  infillPercent: number | null
  supportsRequired: boolean | null
  materialType: string | null
  printProfileNotes: string | null
}

export type ProfileField = 'layerHeight' | 'infill'

export function emptyProfileForm(): ProfileFormValues {
  return { layerHeight: '', infill: '', supports: '', materialType: '', profileNotes: '' }
}

export function profileToForm(profile: PrintProfile): ProfileFormValues {
  return {
    layerHeight: profile.layerHeightMillimetres === null ? '' : String(profile.layerHeightMillimetres).replace('.', ','),
    infill: profile.infillPercent === null ? '' : String(profile.infillPercent),
    supports: profile.supportsRequired === null ? '' : profile.supportsRequired ? 'yes' : 'no',
    materialType: profile.materialType ?? '',
    profileNotes: profile.printProfileNotes ?? '',
  }
}

export function validateProfileForm(values: ProfileFormValues): { profile: ValidatedProfile | null, errors: Partial<Record<ProfileField, 'number' | 'range'>> } {
  const errors: Partial<Record<ProfileField, 'number' | 'range'>> = {}
  let layerHeight: number | null = null
  let infill: number | null = null

  if (values.layerHeight.trim()) {
    layerHeight = parseDecimal(values.layerHeight)
    if (layerHeight === null) errors.layerHeight = 'number'
    else if (layerHeight < 0.04 || layerHeight > 1) errors.layerHeight = 'range'
  }
  if (values.infill.trim()) {
    infill = parseDecimal(values.infill)
    if (infill === null || !Number.isInteger(infill)) errors.infill = 'number'
    else if (infill < 0 || infill > 100) errors.infill = 'range'
  }
  if (Object.keys(errors).length > 0) return { profile: null, errors }

  return {
    profile: {
      layerHeightMillimetres: layerHeight,
      infillPercent: infill,
      supportsRequired: values.supports === '' ? null : values.supports === 'yes',
      materialType: values.materialType.trim() || null,
      printProfileNotes: values.profileNotes.trim() || null,
    },
    errors,
  }
}

export function appendProfile(form: FormData, profile: ValidatedProfile) {
  if (profile.layerHeightMillimetres !== null) form.append('layerHeightMillimetres', String(profile.layerHeightMillimetres))
  if (profile.infillPercent !== null) form.append('infillPercent', String(profile.infillPercent))
  if (profile.supportsRequired !== null) form.append('supportsRequired', String(profile.supportsRequired))
  if (profile.materialType) form.append('materialType', profile.materialType)
  if (profile.printProfileNotes) form.append('printProfileNotes', profile.printProfileNotes)
}

export function hasProfile(profile: PrintProfile): boolean {
  return profile.layerHeightMillimetres !== null
    || profile.infillPercent !== null
    || profile.supportsRequired !== null
    || !!profile.materialType
    || !!profile.printProfileNotes
}
