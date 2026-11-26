import { describe, expect, it } from 'vitest'
import { depreciationPerHour, printerToForm, validatePrinterForm } from '../utils/printerForm'

const a1 = {
  name: 'A1 desk',
  model: 'Bambu Lab A1',
  buildVolumeXMillimetres: 256,
  buildVolumeYMillimetres: 256,
  buildVolumeZMillimetres: 256,
  nozzleDiameterMillimetres: 0.4,
  powerDrawWatts: 95,
  purchaseCost: 339,
  lifetimeHours: 5000,
}

describe('validatePrinterForm', () => {
  it('round-trips a printer through the form', () => {
    expect(validatePrinterForm(printerToForm(a1)).printer).toEqual(a1)
  })

  it('accepts a decimal comma for the nozzle and cost', () => {
    const form = { ...printerToForm(a1), nozzle: '0,6', cost: '349,50' }
    expect(validatePrinterForm(form).printer).toMatchObject({ nozzleDiameterMillimetres: 0.6, purchaseCost: 349.5 })
  })

  it('flags required, number and range problems per field', () => {
    const form = { ...printerToForm(a1), name: ' ', volumeX: '12.5', power: '0', lifetime: 'abc' }
    const { printer, errors } = validatePrinterForm(form)
    expect(printer).toBeNull()
    expect(errors).toEqual({ name: 'required', volumeX: 'number', power: 'range', lifetime: 'number' })
  })
})

describe('depreciationPerHour', () => {
  it('divides the purchase cost over the lifetime', () => {
    expect(depreciationPerHour(500, 4000)).toBe(0.125)
  })

  it('is zero without a lifetime', () => {
    expect(depreciationPerHour(500, 0)).toBe(0)
  })
})
