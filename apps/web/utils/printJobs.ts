import { parseDecimal } from './settingsForm'

export type PrintJobStatus = 'Planned' | 'Printing' | 'Done' | 'Failed' | 'Cancelled'

export const printJobStatuses: PrintJobStatus[] = ['Planned', 'Printing', 'Done', 'Failed', 'Cancelled']
export const finishedStatuses: PrintJobStatus[] = ['Done', 'Failed', 'Cancelled']

export interface PrintJobFilament {
  spoolId: string
  materialName: string
  colorName: string
  colorHex: string | null
  grams: number
  wasteGrams: number
}

export interface PrintJob {
  id: string
  printerId: string
  printerName: string
  name: string
  status: PrintJobStatus
  startedAt: string | null
  finishedAt: string | null
  durationMinutes: number | null
  notes: string | null
  finishedUnits: number | null
  externalSource: string | null
  externalId: string | null
  modelId: string | null
  totalGrams: number
  wasteGrams: number
  createdAt: string
  filaments: PrintJobFilament[]
  orderLineId?: string | null
  orderId?: string | null
  orderNumber?: number | null
  orderLineDescription?: string | null
  plannedUnits?: number | null
  queuePosition?: number | null
  requeuedFromId?: string | null
  requeued?: boolean
}

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

const tones: Record<PrintJobStatus, StatusTone> = {
  Planned: 'neutral',
  Printing: 'info',
  Done: 'success',
  Failed: 'danger',
  Cancelled: 'warning',
}

export function statusTone(status: PrintJobStatus): StatusTone {
  return tones[status]
}

export function isFinished(status: PrintJobStatus): boolean {
  return finishedStatuses.includes(status)
}

export function formatDuration(minutes: number | null): string {
  if (minutes === null) return ''
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

export interface PrintJobFilters {
  status: string
  printerId: string
  search: string
}

export function filterPrintJobs(jobs: PrintJob[], filters: PrintJobFilters): PrintJob[] {
  const needle = filters.search.trim().toLowerCase()
  return jobs.filter((job) => {
    if (filters.status && job.status !== filters.status) return false
    if (filters.printerId && job.printerId !== filters.printerId) return false
    if (!needle) return true
    return [job.name, job.printerName, ...job.filaments.map(line => `${line.materialName} ${line.colorName}`)]
      .some(text => text.toLowerCase().includes(needle))
  })
}

export function toLocalInput(iso: string | null): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

export function fromLocalInput(value: string): string | null {
  if (!value.trim()) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

export interface JobLineValues {
  key: number
  spoolId: string
  grams: string
  wasteGrams: string
}

export interface JobFormValues {
  name: string
  printerId: string
  modelId: string
  status: PrintJobStatus
  startedAt: string
  finishedAt: string
  finishedUnits: string
  notes: string
  lines: JobLineValues[]
}

export type JobErrorKind = 'required' | 'number' | 'range' | 'date' | 'order' | 'duplicate' | 'waste'

export interface PrintJobBody {
  printerId: string
  name: string
  status: PrintJobStatus
  startedAt: string | null
  finishedAt: string | null
  durationMinutes: null
  notes: string | null
  finishedUnits: number | null
  externalSource: string | null
  externalId: string | null
  modelId: string | null
  filaments: { spoolId: string, grams: number, wasteGrams: number }[]
}

export function validateJobForm(
  values: JobFormValues,
  external: { externalSource: string | null, externalId: string | null } = { externalSource: null, externalId: null },
): { body: PrintJobBody | null, errors: Record<string, JobErrorKind> } {
  const errors: Record<string, JobErrorKind> = {}
  const finished = isFinished(values.status)

  if (!values.name.trim()) errors.name = 'required'
  else if (values.name.trim().length > 150) errors.name = 'range'
  if (!values.printerId) errors.printerId = 'required'

  const startedAt = fromLocalInput(values.startedAt)
  if (values.startedAt.trim() && !startedAt) errors.startedAt = 'date'
  else if (values.status === 'Printing' && !startedAt) errors.startedAt = 'required'

  const finishedAt = finished ? fromLocalInput(values.finishedAt) : null
  if (finished && values.finishedAt.trim() && !finishedAt) errors.finishedAt = 'date'
  else if (startedAt && finishedAt && finishedAt < startedAt) errors.finishedAt = 'order'

  let finishedUnits: number | null = null
  if (values.status === 'Done' && values.finishedUnits.trim()) {
    const parsed = parseDecimal(values.finishedUnits)
    if (parsed === null || !Number.isInteger(parsed)) errors.finishedUnits = 'number'
    else if (parsed > 100_000) errors.finishedUnits = 'range'
    else finishedUnits = parsed
  }

  if (values.notes.trim().length > 2000) errors.notes = 'range'

  const seen = new Set<string>()
  const filaments: PrintJobBody['filaments'] = []
  values.lines.forEach((line, index) => {
    const prefix = `lines.${index}`
    if (!line.spoolId) errors[`${prefix}.spoolId`] = 'required'
    else if (seen.has(line.spoolId)) errors[`${prefix}.spoolId`] = 'duplicate'
    seen.add(line.spoolId)

    const grams = parseDecimal(line.grams)
    if (grams === null || !Number.isInteger(grams)) errors[`${prefix}.grams`] = 'number'
    else if (grams < 1 || grams > 100_000) errors[`${prefix}.grams`] = 'range'

    let waste = 0
    if (line.wasteGrams.trim()) {
      const parsedWaste = parseDecimal(line.wasteGrams)
      if (parsedWaste === null || !Number.isInteger(parsedWaste)) errors[`${prefix}.wasteGrams`] = 'number'
      else if (grams !== null && parsedWaste > grams) errors[`${prefix}.wasteGrams`] = 'waste'
      else waste = parsedWaste
    }

    if (!errors[`${prefix}.spoolId`] && !errors[`${prefix}.grams`] && !errors[`${prefix}.wasteGrams`]) {
      filaments.push({ spoolId: line.spoolId, grams: grams!, wasteGrams: waste })
    }
  })

  if (Object.keys(errors).length > 0) return { body: null, errors }

  return {
    body: {
      printerId: values.printerId,
      name: values.name.trim(),
      status: values.status,
      startedAt,
      finishedAt,
      durationMinutes: null,
      notes: values.notes.trim() || null,
      finishedUnits,
      externalSource: external.externalSource,
      externalId: external.externalId,
      modelId: values.modelId || null,
      filaments,
    },
    errors,
  }
}

export function jobToFormValues(job: PrintJob): JobFormValues {
  return {
    name: job.name,
    printerId: job.printerId,
    modelId: job.modelId ?? '',
    status: job.status,
    startedAt: toLocalInput(job.startedAt),
    finishedAt: toLocalInput(job.finishedAt),
    finishedUnits: job.finishedUnits === null ? '' : String(job.finishedUnits),
    notes: job.notes ?? '',
    lines: job.filaments.map((line, index) => ({
      key: index,
      spoolId: line.spoolId,
      grams: String(line.grams),
      wasteGrams: line.wasteGrams ? String(line.wasteGrams) : '',
    })),
  }
}

export function mapServerErrors(fieldErrors: Record<string, string[]>, localize: (serverMessage: string) => string): { fields: Record<string, string>, hasGeneral: boolean } {
  const fields: Record<string, string> = {}
  let hasGeneral = false
  const known = ['name', 'printerId', 'modelId', 'status', 'startedAt', 'finishedAt', 'finishedUnits', 'notes']
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const line = /^filaments\[(\d+)]\.(\w+)$/.exec(key)
    if (line) fields[`lines.${line[1]}.${line[2]}`] = localize(messages[0] ?? '')
    else if (known.includes(key)) fields[key] = localize(messages[0] ?? '')
    else hasGeneral = true
  }
  return { fields, hasGeneral }
}
