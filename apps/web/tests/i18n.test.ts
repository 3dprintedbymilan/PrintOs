import { readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const sourceDirs = ['pages', 'components', 'utils', 'composables', 'stores', 'layouts', 'middleware', 'plugins']

type Tree = { [key: string]: string | Tree }

function loadLocale(code: string): Tree {
  const raw = readFileSync(join(root, 'i18n', 'locales', `${code}.json`), 'utf8').replace(new RegExp('^' + String.fromCharCode(0xfeff)), '')
  return JSON.parse(raw) as Tree
}

function flatten(tree: Tree, prefix = ''): Record<string, string> {
  const result: Record<string, string> = {}
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'string') result[path] = value
    else Object.assign(result, flatten(value, path))
  }
  return result
}

function sourceFiles(dir: string): string[] {
  const absolute = join(root, dir)
  const files: string[] = []
  for (const entry of readdirSync(absolute)) {
    const full = join(absolute, entry)
    if (statSync(full).isDirectory()) files.push(...sourceFiles(relative(root, full)))
    else if (/\.(vue|ts)$/.test(entry)) files.push(full)
  }
  return files
}

const nl = flatten(loadLocale('nl'))
const en = flatten(loadLocale('en'))
const namespaces = new Set(Object.keys(nl).map(key => key.split('.')[0]))

function collectReferences() {
  const exact = new Map<string, string>()
  const prefixes = new Map<string, string>()
  const names = [...namespaces].join('|')
  const openQuote = '(?:(?<!=)"|\'|`)'
  const closeQuote = '[\'"`]'
  const exactPattern = new RegExp(`${openQuote}((?:${names})(?:\\.[A-Za-z0-9_-]+)+)${closeQuote}`, 'g')
  const prefixPattern = new RegExp(`${openQuote}((?:${names})\\.(?:[A-Za-z0-9_-]+\\.)*)\\$\\{`, 'g')
  const files = [...sourceDirs.flatMap(sourceFiles), join(root, 'app.vue')]
  for (const file of files) {
    const text = readFileSync(file, 'utf8')
    const where = relative(root, file)
    for (const match of text.matchAll(exactPattern)) exact.set(match[1] ?? '', where)
    for (const match of text.matchAll(prefixPattern)) prefixes.set(match[1] ?? '', where)
  }
  return { exact, prefixes }
}

function placeholders(message: string): string[] {
  return [...message.matchAll(/\{(\w+)\}/g)].map(match => match[1] ?? '').sort()
}

describe('i18n catalogues', () => {
  it('nl and en define exactly the same keys', () => {
    const missingInEn = Object.keys(nl).filter(key => !(key in en))
    const missingInNl = Object.keys(en).filter(key => !(key in nl))
    expect({ missingInEn, missingInNl }).toEqual({ missingInEn: [], missingInNl: [] })
  })

  it('has no empty values', () => {
    const empty = [...Object.entries(nl).map(([k, v]) => ['nl', k, v]), ...Object.entries(en).map(([k, v]) => ['en', k, v])]
      .filter(([, , value]) => !String(value).trim())
      .map(([locale, key]) => `${locale}:${key}`)
    expect(empty).toEqual([])
  })

  it('uses the same placeholders in both languages', () => {
    const mismatched = Object.keys(nl)
      .filter(key => key in en && placeholders(nl[key] ?? '').join() !== placeholders(en[key] ?? '').join())
    expect(mismatched).toEqual([])
  })

  it('uses the same plural arity in both languages', () => {
    const mismatched = Object.keys(nl)
      .filter(key => key in en && (nl[key] ?? '').split(' | ').length !== (en[key] ?? '').split(' | ').length)
    expect(mismatched).toEqual([])
  })

  it('does not let Dutch copy fall back to English text', () => {
    const identicalLongCopy = Object.keys(nl)
      .filter(key => nl[key] === en[key] && (nl[key] ?? '').length > 40)
    expect(identicalLongCopy).toEqual([])
  })

  it('every key referenced from code exists', () => {
    const { exact } = collectReferences()
    const missing = [...exact.entries()].filter(([key]) => !(key in nl)).map(([key, file]) => `${key} (${file})`)
    expect(missing).toEqual([])
  })

  it('every dynamic key prefix matches at least one key', () => {
    const { prefixes } = collectReferences()
    const dangling = [...prefixes.entries()].filter(([prefix]) => !Object.keys(nl).some(key => key.startsWith(prefix))).map(([prefix, file]) => `${prefix} (${file})`)
    expect(dangling).toEqual([])
  })

  it('has no unused keys', () => {
    const { exact, prefixes } = collectReferences()
    const unused = Object.keys(nl).filter(key => !exact.has(key) && ![...prefixes.keys()].some(prefix => key.startsWith(prefix)))
    expect(unused).toEqual([])
  })
})
