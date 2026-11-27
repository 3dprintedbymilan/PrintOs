import type { PrintJobStatus } from './printJobs'
import { parseDecimal } from './settingsForm'

export interface QueueFilamentCheck {
  spoolId: string
  materialName: string
  colorName: string
  colorHex: string | null
  neededGrams: number
  availableGrams: number
  archived: boolean
  enough: boolean
}

export interface QueueItem {
  id: string
  name: string
  status: PrintJobStatus
  printerId: string
  printerName: string
  plannedUnits: number | null
  durationMinutes: number | null
  startedAt: string | null
  queuePosition: number | null
  orderId: string | null
  orderNumber: number | null
  customerName: string | null
  dueDate: string | null
  isOverdue: boolean
  orderLineId: string | null
  lineDescription: string | null
  lineQuantity: number | null
  hasFilament: boolean
  filamentShort: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  filaments: QueueFilamentCheck[]
}

export interface Queue {
  items: QueueItem[]
  estimatedMinutes: number
  withoutEstimate: number
  manualOrder: boolean
}

export interface ProductionLineJob {
  id: string
  name: string
  status: PrintJobStatus
  printerName: string
  plannedUnits: number | null
  finishedUnits: number | null
  requeued: boolean
  requeuedFromId: string | null
}

export interface ProductionLine {
  lineId: string
  description: string
  ordered: number
  produced: number
  queued: number
  remaining: number
  complete: boolean
  jobs: ProductionLineJob[]
}

export interface OrderProduction {
  orderId: string
  ordered: number
  produced: number
  queued: number
  percent: number
  complete: boolean
  failedJobs: number
  lines: ProductionLine[]
}

export interface ProductionSuggestion {
  orderId: string
  orderNumber: number
  customerName: string | null
  dueDate: string | null
  isOverdue: boolean
  status: string
  lines: ProductionLine[]
}

export type MoveDirection = 'up' | 'down' | 'top'

export function lineProgressPercent(line: Pick<ProductionLine, 'ordered' | 'produced'>): number {
  if (line.ordered <= 0) return 0
  return Math.min(100, Math.floor((line.produced * 100) / line.ordered))
}

export function splitCount(units: number, unitsPerJob: number | null): number {
  if (units <= 0) return 0
  const size = unitsPerJob && unitsPerJob > 0 ? Math.min(unitsPerJob, units) : units
  return Math.ceil(units / size)
}

export function parseUnitsPerJob(value: string): { value: number | null, valid: boolean } {
  if (!value.trim()) return { value: null, valid: true }
  const parsed = parseDecimal(value)
  if (parsed === null || !Number.isInteger(parsed) || parsed < 1 || parsed > 100_000) return { value: null, valid: false }
  return { value: parsed, valid: true }
}

export function parseUnits(value: string): number | null {
  const parsed = parseDecimal(value)
  return parsed !== null && Number.isInteger(parsed) && parsed >= 1 && parsed <= 100_000 ? parsed : null
}

export function filamentIssues(item: QueueItem): QueueFilamentCheck[] {
  return item.filaments.filter(check => !check.enough)
}

export function printingItems(items: QueueItem[]): QueueItem[] {
  return items.filter(item => item.status === 'Printing')
}

export function plannedItems(items: QueueItem[]): QueueItem[] {
  return items.filter(item => item.status === 'Planned')
}
