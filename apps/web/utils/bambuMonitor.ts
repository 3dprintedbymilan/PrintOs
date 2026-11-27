import type { BambuConnectionStatus, BambuPrinterStatus } from '~/utils/bambu'

export interface BambuUtilizationWindow {
  hours: number
  printingPercent: number | null
  printingMinutes: number
  observedMinutes: number
  offlineMinutes: number
  coveragePercent: number
}

export interface BambuTimelineEntry {
  state: BambuPrinterStatus['state']
  jobName: string | null
  at: string
  durationMinutes: number | null
}

export interface BambuTrayInfo {
  id: string
  type: string | null
  color: string | null
  current: boolean
}

export interface BambuMonitorPrinter extends BambuPrinterStatus {
  model: string
  lastKnownState: BambuPrinterStatus['state'] | null
  online: boolean
  stale: boolean
  printErrorCode: string | null
  hmsCodes: string[]
  trays: BambuTrayInfo[]
  utilization: BambuUtilizationWindow[]
  timeline: BambuTimelineEntry[]
}

export interface BambuMonitor {
  connected: boolean
  connectionStatus: BambuConnectionStatus | null
  serverTime: string
  printers: BambuMonitorPrinter[]
}

export const utilizationWindows = [24, 168, 720] as const

export const cameraDocsUrl = 'https://github.com/3dprintedbymilan/PrintOs/blob/dev/docs/bambu-spike.md'

export function windowFor(printer: BambuMonitorPrinter, hours: number): BambuUtilizationWindow | null {
  return printer.utilization.find(window => window.hours === hours) ?? null
}

export function formatMinutes(minutes: number): string {
  if (minutes < 1) return '0 min'
  const hours = Math.floor(minutes / 60)
  const rest = Math.round(minutes % 60)
  if (hours >= 48) return `${Math.round(hours / 24)} d`
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

export function relativeAge(iso: string | null, now: number, locale: string): string {
  if (!iso) return ''
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000)
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
  const absolute = Math.abs(seconds)
  if (absolute < 60) return formatter.format(0, 'second')
  if (absolute < 3600) return formatter.format(Math.round(seconds / 60), 'minute')
  if (absolute < 86400) return formatter.format(Math.round(seconds / 3600), 'hour')
  return formatter.format(Math.round(seconds / 86400), 'day')
}

export function hasHealthProblem(printer: BambuMonitorPrinter): boolean {
  return Boolean(printer.printErrorCode) || printer.hmsCodes.length > 0
}
