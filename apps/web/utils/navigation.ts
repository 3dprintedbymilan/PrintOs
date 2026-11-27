export type NavIconName
  = | 'home' | 'prints' | 'inventory' | 'business'
    | 'printers' | 'bambu' | 'models' | 'licenses' | 'products' | 'calculator' | 'settings' | 'audit' | 'customers' | 'orders' | 'queue'

export interface Destination {
  to: string
  icon: NavIconName
  labelKey: string
  matches: string[]
}

export const primaryDestinations: Destination[] = [
  { to: '/', icon: 'home', labelKey: 'nav.home', matches: [] },
  { to: '/prints', icon: 'prints', labelKey: 'nav.prints', matches: ['/prints', '/print-jobs', '/models'] },
  { to: '/inventory', icon: 'inventory', labelKey: 'nav.inventory', matches: ['/inventory'] },
  {
    to: '/business',
    icon: 'business',
    labelKey: 'nav.business',
    matches: ['/business', '/settings', '/printers', '/products', '/calculator', '/audit', '/licenses', '/orders', '/customers'],
  },
]

export const secondaryDestinations: Destination[] = [
  { to: '/inventory/finished', icon: 'inventory', labelKey: 'finished.title', matches: ['/inventory/finished'] },
  { to: '/inventory/count', icon: 'inventory', labelKey: 'stockTools.count.title', matches: ['/inventory/count'] },
  { to: '/inventory/reservations', icon: 'inventory', labelKey: 'stockTools.reservations.title', matches: ['/inventory/reservations'] },
  { to: '/printers', icon: 'printers', labelKey: 'business.printers', matches: ['/printers'] },
  { to: '/printers/bambu', icon: 'bambu', labelKey: 'bambu.title', matches: ['/printers/bambu'] },
  { to: '/prints?section=queue', icon: 'queue', labelKey: 'queue.title', matches: [] },
  { to: '/prints?section=models', icon: 'models', labelKey: 'prints.models', matches: ['/models'] },
  { to: '/licenses', icon: 'licenses', labelKey: 'business.licenses', matches: ['/licenses'] },
  { to: '/orders', icon: 'orders', labelKey: 'business.orders', matches: ['/orders'] },
  { to: '/customers', icon: 'customers', labelKey: 'business.customers', matches: ['/customers'] },
  { to: '/products', icon: 'products', labelKey: 'business.products', matches: ['/products'] },
  { to: '/calculator', icon: 'calculator', labelKey: 'business.calculator', matches: ['/calculator'] },
  { to: '/settings', icon: 'settings', labelKey: 'business.settings', matches: ['/settings'] },
  { to: '/audit', icon: 'audit', labelKey: 'business.audit', matches: ['/audit'] },
]

function startsWithSegment(path: string, prefix: string) {
  return path === prefix || path.startsWith(`${prefix}/`)
}

export function isPrimaryActive(destination: Destination, path: string) {
  if (destination.to === '/') return path === '/'
  return destination.matches.some(prefix => startsWithSegment(path, prefix))
}

export function activeSecondary(path: string, query: Record<string, unknown> = {}) {
  if (path === '/prints') {
    if (query.section === 'models') return '/prints?section=models'
    return query.section === 'queue' ? '/prints?section=queue' : null
  }
  let best: Destination | null = null
  let bestLength = -1
  for (const destination of secondaryDestinations) {
    for (const prefix of destination.matches) {
      if (startsWithSegment(path, prefix) && prefix.length > bestLength) {
        best = destination
        bestLength = prefix.length
      }
    }
  }
  return best?.to ?? null
}
