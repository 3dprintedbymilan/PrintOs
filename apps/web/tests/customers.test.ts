import { describe, expect, it } from 'vitest'
import { customerContactLine, customerToForm, emptyCustomerForm, validateCustomerForm } from '../utils/customers'

describe('validateCustomerForm', () => {
  it('requires a name', () => {
    expect(validateCustomerForm(emptyCustomerForm())).toMatchObject({ customer: null, errors: { name: 'required' } })
  })

  it('trims values and turns empty optional fields into null', () => {
    const { customer } = validateCustomerForm({ ...emptyCustomerForm(), name: '  Jan  ', email: ' jan@example.com ', phone: '  ' })
    expect(customer).toEqual({ name: 'Jan', email: 'jan@example.com', phone: null, address: null, vatNumber: null, notes: null })
  })

  it('rejects an invalid email and over-long fields', () => {
    const { customer, errors } = validateCustomerForm({ ...emptyCustomerForm(), name: 'Jan', email: 'jan@', notes: 'x'.repeat(2001) })
    expect(customer).toBeNull()
    expect(errors).toEqual({ email: 'email', notes: 'tooLong' })
  })

  it('round-trips a customer through the form', () => {
    const request = { name: 'Jan', email: null, phone: '0470', address: 'Kerkstraat 1', vatNumber: 'BE0123456789', notes: null }
    expect(validateCustomerForm(customerToForm(request)).customer).toEqual(request)
  })
})

describe('customerContactLine', () => {
  it('joins the available contact details', () => {
    expect(customerContactLine({ email: 'a@b.be', phone: '0470' })).toBe('a@b.be · 0470')
    expect(customerContactLine({ email: null, phone: '0470' })).toBe('0470')
    expect(customerContactLine({ email: null, phone: null })).toBe('')
  })
})
