import { describe, expect, it } from 'vitest'
import { formatDecimal, parseDecimal, toFormValues, validateSettingsForm } from '../utils/settingsForm'

const valid = {
  name: ' Milan 3D ',
  currency: 'EUR',
  electricityPricePerKwh: '0,30',
  labourRatePerHour: '20',
  machineRatePerHour: '1.5',
  wastePercent: '5',
  language: 'nl',
}

describe('parseDecimal', () => {
  it('accepts a comma or a dot as decimal separator', () => {
    expect(parseDecimal('0,30')).toBe(0.3)
    expect(parseDecimal(' 2.5 ')).toBe(2.5)
    expect(parseDecimal('20')).toBe(20)
  })

  it('rejects empty, negative and non-numeric input', () => {
    expect(parseDecimal('')).toBeNull()
    expect(parseDecimal('-1')).toBeNull()
    expect(parseDecimal('abc')).toBeNull()
    expect(parseDecimal('1,2,3')).toBeNull()
  })
})

describe('formatDecimal', () => {
  it('shows a comma', () => {
    expect(formatDecimal(0.3)).toBe('0,3')
  })
})

describe('validateSettingsForm', () => {
  it('returns trimmed, parsed settings for valid input', () => {
    const { settings, errors } = validateSettingsForm(valid)
    expect(errors).toEqual({})
    expect(settings).toEqual({
      name: 'Milan 3D',
      currency: 'EUR',
      electricityPricePerKwh: 0.3,
      labourRatePerHour: 20,
      machineRatePerHour: 1.5,
      wastePercent: 5,
      language: 'nl',
    })
  })

  it('reports required, number and range errors per field', () => {
    const { settings, errors } = validateSettingsForm({ ...valid, name: ' ', labourRatePerHour: 'x', wastePercent: '101' })
    expect(settings).toBeNull()
    expect(errors).toEqual({ name: 'required', labourRatePerHour: 'number', wastePercent: 'range' })
  })

  it('round-trips server values into form text', () => {
    const form = toFormValues({ name: 'A', currency: 'USD', electricityPricePerKwh: 0.25, labourRatePerHour: 35, machineRatePerHour: 2.5, wastePercent: 8, language: 'en' })
    expect(form.electricityPricePerKwh).toBe('0,25')
    expect(validateSettingsForm(form).settings?.electricityPricePerKwh).toBe(0.25)
  })
})
