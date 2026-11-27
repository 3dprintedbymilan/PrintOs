export type BambuPrinterState = 'idle' | 'printing' | 'paused' | 'finished' | 'failed' | 'offline'
export type BambuConnectionStatus = 'connected' | 'tokenExpired' | 'unreachable'
export type ChipTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

export interface BambuDevice {
  serial: string
  name: string
  model: string
  online: boolean
  printerId: string | null
  printerName: string | null
}

export interface BambuConnection {
  connected: boolean
  region: string | null
  status: BambuConnectionStatus | null
  accountName: string | null
  connectedAt: string | null
  lastSyncAt: string | null
  flaggedImports: number
  devices: BambuDevice[]
}

export interface BambuPrinterStatus {
  printerId: string
  printerName: string
  serial: string
  state: BambuPrinterState
  jobName: string | null
  progressPercent: number | null
  remainingMinutes: number | null
  layer: number | null
  totalLayers: number | null
  nozzleTemperature: number | null
  bedTemperature: number | null
  filamentType: string | null
  filamentColor: string | null
  lastSeenAt: string | null
}

export interface BambuStatus {
  connected: boolean
  connectionStatus: BambuConnectionStatus | null
  printers: BambuPrinterStatus[]
}

export const pollIntervalMilliseconds = 10_000

const tones: Record<BambuPrinterState, ChipTone> = {
  idle: 'neutral',
  printing: 'info',
  paused: 'warning',
  finished: 'success',
  failed: 'danger',
  offline: 'neutral',
}

export function stateTone(state: BambuPrinterState): ChipTone {
  return tones[state] ?? 'neutral'
}

export function formatRemaining(minutes: number | null): string {
  if (minutes === null || minutes < 0) return ''
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

export function isActivePrint(printer: BambuPrinterStatus): boolean {
  return printer.state === 'printing' || printer.state === 'paused'
}

export function normalizeToken(value: string): string {
  const trimmed = value.trim()
  return /^bearer\s+/i.test(trimmed) ? trimmed.replace(/^bearer\s+/i, '').trim() : trimmed
}

export function validateToken(value: string): 'required' | 'spaces' | null {
  const token = normalizeToken(value)
  if (!token) return 'required'
  return /\s/.test(token) ? 'spaces' : null
}

export function clampPercent(value: number | null): number {
  if (value === null || Number.isNaN(value)) return 0
  return Math.min(100, Math.max(0, Math.round(value)))
}
