import type { ModelVersion } from './modelLibrary'
import { parseStl } from './stlParser'

export class ViewerUnsupportedError extends Error {}

export interface ModelViewerHandle {
  capture: () => Promise<Blob | null>
  dispose: () => void
}

export async function mountModelViewer(host: HTMLElement, buffer: ArrayBuffer, format: ModelVersion['format']): Promise<ModelViewerHandle> {
  const THREE = await import('three')
  const { OrbitControls } = await import('three/examples/jsm/controls/OrbitControls.js')

  let renderer: InstanceType<typeof THREE.WebGLRenderer>
  try {
    renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true })
  }
  catch {
    throw new ViewerUnsupportedError('WebGL is not available.')
  }

  const material = new THREE.MeshStandardMaterial({ color: 0x34d399, roughness: 0.55, metalness: 0.05 })
  const disposables: { dispose: () => void }[] = [material]
  const model = new THREE.Group()

  try {
    if (format === 'stl') {
      const mesh = parseStl(buffer)
      const geometry = new THREE.BufferGeometry()
      geometry.setAttribute('position', new THREE.BufferAttribute(mesh.positions, 3))
      geometry.computeVertexNormals()
      disposables.push(geometry)
      model.add(new THREE.Mesh(geometry, material))
    }
    else if (format === '3mf') {
      const { ThreeMFLoader } = await import('three/examples/jsm/loaders/3MFLoader.js')
      model.add(new ThreeMFLoader().parse(buffer))
    }
    else {
      const { OBJLoader } = await import('three/examples/jsm/loaders/OBJLoader.js')
      model.add(new OBJLoader().parse(new TextDecoder().decode(buffer)))
    }
  }
  catch (error) {
    renderer.dispose()
    throw error
  }

  model.traverse((child) => {
    const mesh = child as InstanceType<typeof THREE.Mesh>
    if (mesh.isMesh) mesh.material = material
  })

  const scene = new THREE.Scene()
  const holder = new THREE.Group()
  if (format !== 'obj') holder.rotation.x = -Math.PI / 2
  holder.add(model)
  scene.add(holder)
  holder.updateMatrixWorld(true)

  const box = new THREE.Box3().setFromObject(holder)
  const size = box.getSize(new THREE.Vector3())
  const center = box.getCenter(new THREE.Vector3())
  const radius = Math.max(size.x, size.y, size.z, 1e-6)
  holder.position.sub(center)
  holder.position.y += size.y / 2

  const grid = new THREE.GridHelper(radius * 2, 20, 0x3b6b58, 0x1f3a30)
  scene.add(grid)
  scene.add(new THREE.HemisphereLight(0xffffff, 0x1a2a24, 1.6))
  const sun = new THREE.DirectionalLight(0xffffff, 2.2)
  sun.position.set(radius, radius * 2, radius)
  scene.add(sun)

  const camera = new THREE.PerspectiveCamera(40, 1, radius / 100, radius * 20)
  camera.position.set(radius * 2.2, radius * 1.7, radius * 2.5)
  const controls = new OrbitControls(camera, renderer.domElement)
  controls.target.set(0, size.y / 2, 0)
  controls.update()

  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2))
  renderer.domElement.style.display = 'block'
  renderer.domElement.style.touchAction = 'none'
  host.appendChild(renderer.domElement)

  let frame = 0
  const render = () => {
    frame = 0
    renderer.render(scene, camera)
  }
  const schedule = () => {
    if (!frame) frame = requestAnimationFrame(render)
  }
  controls.addEventListener('change', schedule)

  const resize = () => {
    const width = Math.max(host.clientWidth, 1)
    const height = Math.max(host.clientHeight, 1)
    renderer.setSize(width, height, false)
    renderer.domElement.style.width = '100%'
    renderer.domElement.style.height = '100%'
    camera.aspect = width / height
    camera.updateProjectionMatrix()
    schedule()
  }
  const observer = new ResizeObserver(resize)
  observer.observe(host)
  resize()
  render()

  return {
    capture: () => new Promise((resolve) => {
      render()
      renderer.domElement.toBlob(blob => resolve(blob), 'image/png')
    }),
    dispose: () => {
      if (frame) cancelAnimationFrame(frame)
      observer.disconnect()
      controls.dispose()
      model.traverse((child) => {
        const mesh = child as InstanceType<typeof THREE.Mesh>
        if (mesh.isMesh) mesh.geometry.dispose()
      })
      disposables.forEach(item => item.dispose())
      renderer.dispose()
      renderer.forceContextLoss()
      renderer.domElement.remove()
    },
  }
}
