import { describe, expect, it } from 'vitest'
import { activeSecondary, isPrimaryActive, primaryDestinations } from '../utils/navigation'

const byPath = (to: string) => primaryDestinations.find(d => d.to === to)!

describe('navigation active state', () => {
  it('keeps Home exact', () => {
    expect(isPrimaryActive(byPath('/'), '/')).toBe(true)
    expect(isPrimaryActive(byPath('/'), '/prints')).toBe(false)
  })

  it('highlights Business for the areas that hang off it', () => {
    for (const path of ['/business', '/settings', '/printers/bambu', '/products/abc', '/calculator/saved', '/audit', '/licenses/new']) {
      expect(isPrimaryActive(byPath('/business'), path)).toBe(true)
    }
    expect(isPrimaryActive(byPath('/business'), '/inventory')).toBe(false)
  })

  it('highlights Prints for jobs and models', () => {
    expect(isPrimaryActive(byPath('/prints'), '/print-jobs/1')).toBe(true)
    expect(isPrimaryActive(byPath('/prints'), '/models/1')).toBe(true)
  })

  it('does not match on a shared prefix without a segment boundary', () => {
    expect(isPrimaryActive(byPath('/prints'), '/printers')).toBe(false)
  })

  it('picks the most specific secondary destination', () => {
    expect(activeSecondary('/printers')).toBe('/printers')
    expect(activeSecondary('/printers/bambu')).toBe('/printers/bambu')
    expect(activeSecondary('/printers/123')).toBe('/printers')
    expect(activeSecondary('/models/5')).toBe('/prints?section=models')
    expect(activeSecondary('/prints', { section: 'models' })).toBe('/prints?section=models')
    expect(activeSecondary('/prints')).toBeNull()
    expect(activeSecondary('/business')).toBeNull()
  })
})
