import { formatDecimal, parseDecimal } from './settingsForm'

export interface Printer {
  id: string
  name: string
  model: string
  buildVolumeXMillimetres: number
  buildVolumeYMillimetres: number
  buildVolumeZMillimetres: number
  nozzleDiameterMillimetres: number
  powerDrawWatts: number
  purchaseCost: number
  lifetimeHours: number
  depreciationPerHour: number
  isArchived: boolean
}

export type PrinterRequest = Omit<Printer, 'id' | 'depreciationPerHour' | 'isArchived'>

export type PrinterField = 'name' | 'model' | 'volumeX' | 'volumeY' | 'volumeZ' | 'nozzle' | 'power' | 'cost' | 'lifetime'
export type PrinterErrorKind = 'required' | 'number' | 'range'

export interface PrinterFormValues {
  name: string
  model: string
  volumeX: string
  volumeY: string
  volumeZ: string
  nozzle: string
  power: string
  cost: string
  lifetime: string
}

const limits: Record<Exclude<PrinterField, 'name' | 'model'>, { min: number, max: number, integer: boolean }> = {
  volumeX: { min: 1, max: 2000, integer: true },
  volumeY: { min: 1, max: 2000, integer: true },
  volumeZ: { min: 1, max: 2000, integer: true },
  nozzle: { min: 0.1, max: 2, integer: false },
  power: { min: 1, max: 5000, integer: true },
  cost: { min: 0, max: 1_000_000, integer: false },
  lifetime: { min: 1, max: 100_000, integer: true },
}

export function emptyPrinterForm(): PrinterFormValues {
  return { name: '', model: '', volumeX: '', volumeY: '', volumeZ: '', nozzle: '', power: '', cost: '', lifetime: '' }
}

export function printerToForm(printer: PrinterRequest): PrinterFormValues {
  return {
    name: printer.name,
    model: printer.model,
    volumeX: String(printer.buildVolumeXMillimetres),
    volumeY: String(printer.buildVolumeYMillimetres),
    volumeZ: String(printer.buildVolumeZMillimetres),
    nozzle: formatDecimal(printer.nozzleDiameterMillimetres),
    power: String(printer.powerDrawWatts),
    cost: formatDecimal(printer.purchaseCost),
    lifetime: String(printer.lifetimeHours),
  }
}

export function depreciationPerHour(cost: number, lifetimeHours: number): number {
  return lifetimeHours > 0 ? Math.round((cost / lifetimeHours) * 10000) / 10000 : 0
}

export function validatePrinterForm(values: PrinterFormValues): {
  printer: PrinterRequest | null
  errors: Partial<Record<PrinterField, PrinterErrorKind>>
} {
  const errors: Partial<Record<PrinterField, PrinterErrorKind>> = {}
  if (!values.name.trim()) errors.name = 'required'
  if (!values.model.trim()) errors.model = 'required'

  const numbers: Record<string, number> = {}
  for (const field of Object.keys(limits) as (keyof typeof limits)[]) {
    const { min, max, integer } = limits[field]
    const parsed = parseDecimal(values[field])
    if (parsed === null || (integer && !Number.isInteger(parsed))) errors[field] = 'number'
    else if (parsed < min || parsed > max) errors[field] = 'range'
    else numbers[field] = parsed
  }

  if (Object.keys(errors).length > 0) return { printer: null, errors }

  return {
    printer: {
      name: values.name.trim(),
      model: values.model.trim(),
      buildVolumeXMillimetres: numbers.volumeX!,
      buildVolumeYMillimetres: numbers.volumeY!,
      buildVolumeZMillimetres: numbers.volumeZ!,
      nozzleDiameterMillimetres: numbers.nozzle!,
      powerDrawWatts: numbers.power!,
      purchaseCost: numbers.cost!,
      lifetimeHours: numbers.lifetime!,
    },
    errors,
  }
}
