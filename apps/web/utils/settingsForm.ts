export interface WorkspaceSettings {
  name: string
  currency: string
  electricityPricePerKwh: number
  labourRatePerHour: number
  machineRatePerHour: number
  wastePercent: number
  language: string
}

export type SettingsField = 'name' | 'electricityPricePerKwh' | 'labourRatePerHour' | 'machineRatePerHour' | 'wastePercent'
export type SettingsErrorKind = 'required' | 'number' | 'range'

export const supportedCurrencies = ['EUR', 'USD', 'GBP']
export const supportedLanguages = ['nl', 'en']

const numericLimits = {
  electricityPricePerKwh: 100,
  labourRatePerHour: 10000,
  machineRatePerHour: 10000,
  wastePercent: 100,
} as const

export function parseDecimal(input: string): number | null {
  const normalized = input.trim().replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalized)) return null
  return Number(normalized)
}

export function formatDecimal(value: number): string {
  return String(value).replace('.', ',')
}

export interface SettingsFormValues {
  name: string
  currency: string
  electricityPricePerKwh: string
  labourRatePerHour: string
  machineRatePerHour: string
  wastePercent: string
  language: string
}

export function toFormValues(settings: WorkspaceSettings): SettingsFormValues {
  return {
    name: settings.name,
    currency: settings.currency,
    electricityPricePerKwh: formatDecimal(settings.electricityPricePerKwh),
    labourRatePerHour: formatDecimal(settings.labourRatePerHour),
    machineRatePerHour: formatDecimal(settings.machineRatePerHour),
    wastePercent: formatDecimal(settings.wastePercent),
    language: settings.language,
  }
}

export function validateSettingsForm(values: SettingsFormValues): {
  settings: WorkspaceSettings | null
  errors: Partial<Record<SettingsField, SettingsErrorKind>>
} {
  const errors: Partial<Record<SettingsField, SettingsErrorKind>> = {}
  if (!values.name.trim()) errors.name = 'required'

  const parsed: Record<string, number> = {}
  for (const field of Object.keys(numericLimits) as (keyof typeof numericLimits)[]) {
    const value = parseDecimal(values[field])
    if (value === null) errors[field] = 'number'
    else if (value > numericLimits[field]) errors[field] = 'range'
    else parsed[field] = value
  }

  if (Object.keys(errors).length > 0) return { settings: null, errors }

  return {
    settings: {
      name: values.name.trim(),
      currency: values.currency,
      electricityPricePerKwh: parsed.electricityPricePerKwh!,
      labourRatePerHour: parsed.labourRatePerHour!,
      machineRatePerHour: parsed.machineRatePerHour!,
      wastePercent: parsed.wastePercent!,
      language: values.language,
    },
    errors,
  }
}
