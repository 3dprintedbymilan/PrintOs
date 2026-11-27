export interface Customer {
  id: string
  name: string
  email: string | null
  phone: string | null
  address: string | null
  vatNumber: string | null
  notes: string | null
  isArchived: boolean
  createdAt: string
}

export type CustomerRequest = Omit<Customer, 'id' | 'isArchived' | 'createdAt'>

export type CustomerField = 'name' | 'email' | 'phone' | 'address' | 'vatNumber' | 'notes'
export type CustomerErrorKind = 'required' | 'email' | 'tooLong'

export type CustomerFormValues = Record<CustomerField, string>

export const customerLimits: Record<CustomerField, number> = {
  name: 120,
  email: 254,
  phone: 40,
  address: 500,
  vatNumber: 30,
  notes: 2000,
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function emptyCustomerForm(): CustomerFormValues {
  return { name: '', email: '', phone: '', address: '', vatNumber: '', notes: '' }
}

export function customerToForm(customer: CustomerRequest): CustomerFormValues {
  return {
    name: customer.name,
    email: customer.email ?? '',
    phone: customer.phone ?? '',
    address: customer.address ?? '',
    vatNumber: customer.vatNumber ?? '',
    notes: customer.notes ?? '',
  }
}

function optional(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

export function validateCustomerForm(values: CustomerFormValues): {
  customer: CustomerRequest | null
  errors: Partial<Record<CustomerField, CustomerErrorKind>>
} {
  const errors: Partial<Record<CustomerField, CustomerErrorKind>> = {}
  const name = values.name.trim()
  if (!name) errors.name = 'required'
  else if (name.length > customerLimits.name) errors.name = 'tooLong'

  const email = optional(values.email)
  if (email && (!emailPattern.test(email) || email.length > customerLimits.email)) errors.email = 'email'

  for (const field of ['phone', 'address', 'vatNumber', 'notes'] as const) {
    if (values[field].trim().length > customerLimits[field]) errors[field] = 'tooLong'
  }

  if (Object.keys(errors).length > 0) return { customer: null, errors }

  return {
    customer: {
      name,
      email,
      phone: optional(values.phone),
      address: optional(values.address),
      vatNumber: optional(values.vatNumber),
      notes: optional(values.notes),
    },
    errors,
  }
}

export function customerContactLine(customer: Pick<Customer, 'email' | 'phone'>): string {
  return [customer.email, customer.phone].filter(Boolean).join(' · ')
}
