import { parseDecimal } from './settingsForm'

export type SchedulePriority = 0 | 1 | 2 | 3

export type ScheduleBlockReason = 'NoPrinter' | 'TooLarge' | 'FilamentShort' | 'PrinterUnavailable'

export interface ScheduleItem {
  jobId: string
  name: string
  status: 'Printing' | 'Planned'
  start: string
  end: string
  durationMinutes: number
  durationEstimated: boolean
  pinned: boolean
  pinnedStartAt: string | null
  late: boolean
  noFilament: boolean
  priority: SchedulePriority
  orderNumber: number | null
  dueDate: string | null
  plannedUnits: number | null
}

export interface ScheduleLane {
  printerId: string
  name: string
  archived: boolean
  items: ScheduleItem[]
}

export interface ScheduleBlocked {
  jobId: string
  name: string
  reason: ScheduleBlockReason
  priority: SchedulePriority
  orderNumber: number | null
  dueDate: string | null
  pinned: boolean
  partXMillimetres: number | null
  partYMillimetres: number | null
  partZMillimetres: number | null
}

export interface Schedule {
  generatedAt: string
  horizonEnd: string
  defaultDurationMinutes: number
  lanes: ScheduleLane[]
  blocked: ScheduleBlocked[]
  beyondHorizon: number
  reassigned: number
}

export const priorityValues: SchedulePriority[] = [0, 1, 2, 3]

export const horizonOptions = [7, 14, 30]

export interface DayGroup {
  key: string
  date: Date
  items: ScheduleItem[]
}

function dayKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function groupByDay(items: ScheduleItem[]): DayGroup[] {
  const groups = new Map<string, DayGroup>()
  for (const item of [...items].sort((left, right) => left.start.localeCompare(right.start))) {
    const date = new Date(item.start)
    const key = dayKey(date)
    const group = groups.get(key) ?? { key, date, items: [] }
    group.items.push(item)
    groups.set(key, group)
  }
  return [...groups.values()]
}

export function toDateTimeLocal(value: string | null): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return `${dayKey(date)}T${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

export function fromDateTimeLocal(value: string): string | null {
  if (!value.trim()) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

export interface PartSizeInput {
  x: string
  y: string
  z: string
}

export type PartSizeResult
  = | { kind: 'none' }
    | { kind: 'invalid' }
    | { kind: 'ok', x: number, y: number, z: number }

export function parsePartSize(input: PartSizeInput): PartSizeResult {
  const values = [input.x, input.y, input.z].map(value => value.trim())
  if (values.every(value => value === '')) return { kind: 'none' }
  const parsed = values.map(value => parseDecimal(value))
  if (parsed.some(value => value === null || !Number.isInteger(value) || value < 1 || value > 2000)) return { kind: 'invalid' }
  return { kind: 'ok', x: parsed[0]!, y: parsed[1]!, z: parsed[2]! }
}

export function totalPlanned(lanes: ScheduleLane[]): number {
  return lanes.reduce((sum, lane) => sum + lane.items.filter(item => item.status === 'Planned').length, 0)
}

export function lastEnd(lanes: ScheduleLane[]): string | null {
  const ends = lanes.flatMap(lane => lane.items.map(item => item.end)).sort()
  return ends.length > 0 ? ends[ends.length - 1]! : null
}
