import { describe, expect, it } from 'vitest'
import { emptyProfileForm, formatFileSize, nameFromFileName, parseTags, validateModelFile, validateProfileForm } from '../utils/modelLibrary'
import { meshDimensions, parseStl, StlParseError } from '../utils/stlParser'

function binaryStl(triangles: number[][]): ArrayBuffer {
  const buffer = new ArrayBuffer(84 + triangles.length * 50)
  const view = new DataView(buffer)
  view.setUint32(80, triangles.length, true)
  triangles.forEach((values, index) => {
    values.forEach((value, valueIndex) => view.setFloat32(84 + index * 50 + 12 + valueIndex * 4, value, true))
  })
  return buffer
}

describe('model file validation', () => {
  it('accepts STL, 3MF and OBJ regardless of case', () => {
    expect(validateModelFile({ name: 'Benchy.STL', size: 10 })).toBeNull()
    expect(validateModelFile({ name: 'a.3mf', size: 10 })).toBeNull()
    expect(validateModelFile({ name: 'a.obj', size: 10 })).toBeNull()
  })

  it('rejects other types, empty files and files over the limit', () => {
    expect(validateModelFile({ name: 'a.zip', size: 10 })).toBe('type')
    expect(validateModelFile({ name: 'noextension', size: 10 })).toBe('type')
    expect(validateModelFile({ name: 'a.stl', size: 0 })).toBe('empty')
    expect(validateModelFile({ name: 'a.stl', size: 101 * 1024 * 1024 })).toBe('size')
  })
})

describe('model library helpers', () => {
  it('normalizes tags', () => {
    expect(parseTags(' Boat, boat ; Toy ,, ')).toEqual(['boat', 'toy'])
    expect(parseTags('')).toEqual([])
  })

  it('formats sizes and default names', () => {
    expect(formatFileSize(512)).toBe('512 B')
    expect(formatFileSize(2048)).toBe('2 KB')
    expect(formatFileSize(5 * 1024 * 1024, 'en')).toBe('5.0 MB')
    expect(formatFileSize(5 * 1024 * 1024, 'nl-BE')).toBe('5,0 MB')
    expect(nameFromFileName('3DBenchy.v2.stl')).toBe('3DBenchy.v2')
  })

  it('validates the print profile with decimal commas', () => {
    const ok = validateProfileForm({ ...emptyProfileForm(), layerHeight: '0,2', infill: '15', supports: 'yes', materialType: ' PLA ' })
    expect(ok.profile).toEqual({ layerHeightMillimetres: 0.2, infillPercent: 15, supportsRequired: true, materialType: 'PLA', printProfileNotes: null })
    expect(validateProfileForm({ ...emptyProfileForm(), layerHeight: 'abc' }).errors.layerHeight).toBe('number')
    expect(validateProfileForm({ ...emptyProfileForm(), layerHeight: '2' }).errors.layerHeight).toBe('range')
    expect(validateProfileForm({ ...emptyProfileForm(), infill: '150' }).errors.infill).toBe('range')
    expect(validateProfileForm(emptyProfileForm()).profile?.supportsRequired).toBeNull()
  })
})

describe('STL parsing', () => {
  it('reads binary STL triangles and bounds', () => {
    const mesh = parseStl(binaryStl([[0, 0, 0, 10, 0, 0, 0, 20, 5]]))
    expect(mesh.triangleCount).toBe(1)
    expect(meshDimensions(mesh)).toEqual([10, 20, 5])
  })

  it('reads ASCII STL', () => {
    const text = 'solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid t\n'
    const mesh = parseStl(new TextEncoder().encode(text).buffer as ArrayBuffer)
    expect(mesh.triangleCount).toBe(1)
    expect(mesh.max).toEqual([1, 1, 0])
  })

  it('rejects files that are not STL', () => {
    expect(() => parseStl(new TextEncoder().encode('hello world, not an stl').buffer as ArrayBuffer)).toThrow(StlParseError)
    expect(() => parseStl(new ArrayBuffer(10))).toThrow(StlParseError)
    expect(() => parseStl(new TextEncoder().encode('solid empty\nendsolid empty').buffer as ArrayBuffer)).toThrow(StlParseError)
  })
})
