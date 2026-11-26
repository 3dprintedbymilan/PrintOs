export interface ParsedMesh {
  positions: Float32Array
  triangleCount: number
  min: [number, number, number]
  max: [number, number, number]
}

export class StlParseError extends Error {}

const binaryHeaderBytes = 84
const binaryTriangleBytes = 50

function isBinaryStl(buffer: ArrayBuffer): boolean {
  if (buffer.byteLength < binaryHeaderBytes) return false
  const triangles = new DataView(buffer).getUint32(80, true)
  return binaryHeaderBytes + triangles * binaryTriangleBytes === buffer.byteLength
}

function measure(positions: Float32Array): ParsedMesh {
  const min: [number, number, number] = [Infinity, Infinity, Infinity]
  const max: [number, number, number] = [-Infinity, -Infinity, -Infinity]
  for (let index = 0; index < positions.length; index += 3) {
    for (let axis = 0; axis < 3; axis++) {
      const value = positions[index + axis]!
      if (value < min[axis]!) min[axis] = value
      if (value > max[axis]!) max[axis] = value
    }
  }
  return { positions, triangleCount: positions.length / 9, min, max }
}

function parseBinary(buffer: ArrayBuffer): ParsedMesh {
  const view = new DataView(buffer)
  const triangles = view.getUint32(80, true)
  const positions = new Float32Array(triangles * 9)
  for (let triangle = 0; triangle < triangles; triangle++) {
    const offset = binaryHeaderBytes + triangle * binaryTriangleBytes + 12
    for (let value = 0; value < 9; value++) {
      positions[triangle * 9 + value] = view.getFloat32(offset + value * 4, true)
    }
  }
  return measure(positions)
}

function parseAscii(buffer: ArrayBuffer): ParsedMesh {
  const text = new TextDecoder().decode(buffer)
  const numbers: number[] = []
  const vertex = /vertex\s+(\S+)\s+(\S+)\s+(\S+)/g
  for (let match = vertex.exec(text); match; match = vertex.exec(text)) {
    numbers.push(Number(match[1]), Number(match[2]), Number(match[3]))
  }
  if (numbers.length === 0 || numbers.length % 9 !== 0 || numbers.some(value => !Number.isFinite(value))) {
    throw new StlParseError('The STL file has no valid triangles.')
  }
  return measure(Float32Array.from(numbers))
}

export function parseStl(buffer: ArrayBuffer): ParsedMesh {
  if (isBinaryStl(buffer)) {
    const mesh = parseBinary(buffer)
    if (mesh.triangleCount === 0) throw new StlParseError('The STL file has no triangles.')
    return mesh
  }
  const head = new TextDecoder().decode(new Uint8Array(buffer, 0, Math.min(5, buffer.byteLength)))
  if (head.toLowerCase() !== 'solid') throw new StlParseError('This is not a valid STL file.')
  return parseAscii(buffer)
}

export function meshDimensions(mesh: ParsedMesh): [number, number, number] {
  return [mesh.max[0] - mesh.min[0], mesh.max[1] - mesh.min[1], mesh.max[2] - mesh.min[2]]
}
