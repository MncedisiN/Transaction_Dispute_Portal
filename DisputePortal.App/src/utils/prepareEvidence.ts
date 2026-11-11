import { reference } from '../portalReference'
export interface PreparedEvidence {
  file: File
  original?: File
}
export async function decodeEvidenceImage(
  blob: Blob
): Promise<{ source: CanvasImageSource; width: number; height: number; close: () => void }> {
  if (typeof createImageBitmap === 'function') {
    const bitmap = await createImageBitmap(blob)
    return {
      source: bitmap,
      width: bitmap.width,
      height: bitmap.height,
      close: () => bitmap.close()
    }
  }
  const url = URL.createObjectURL(blob),
    image = new Image()
  try {
    await new Promise<void>((resolve, reject) => {
      image.onload = () => resolve()
      image.onerror = () => reject(Error('This photo could not be decoded. Save a JPEG copy.'))
      image.src = url
    })
    return {
      source: image,
      width: image.naturalWidth,
      height: image.naturalHeight,
      close: () => {
        image.src = ''
        URL.revokeObjectURL(url)
      }
    }
  } catch (e) {
    URL.revokeObjectURL(url)
    throw e
  }
}
export function inspectImageDimensions(
  bytes: Uint8Array
): { width: number; height: number } | null {
  const v = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  if (
    bytes.length >= 24 &&
    bytes[0] === 137 &&
    bytes[1] === 80 &&
    bytes[2] === 78 &&
    bytes[3] === 71
  )
    return { width: v.getUint32(16), height: v.getUint32(20) }
  if (
    bytes.length >= 30 &&
    String.fromCharCode(...bytes.slice(0, 4)) === 'RIFF' &&
    String.fromCharCode(...bytes.slice(8, 12)) === 'WEBP'
  ) {
    const type = String.fromCharCode(...bytes.slice(12, 16))
    if (type === 'VP8X')
      return {
        width: 1 + bytes[24]! + (bytes[25]! << 8) + (bytes[26]! << 16),
        height: 1 + bytes[27]! + (bytes[28]! << 8) + (bytes[29]! << 16)
      }
    if (type === 'VP8 ')
      return { width: v.getUint16(26, true) & 0x3fff, height: v.getUint16(28, true) & 0x3fff }
    if (type === 'VP8L') {
      const bits = v.getUint32(21, true)
      return { width: 1 + (bits & 0x3fff), height: 1 + ((bits >>> 14) & 0x3fff) }
    }
  }
  if (bytes[0] === 255 && bytes[1] === 216) {
    let p = 2
    while (p + 9 < bytes.length) {
      if (bytes[p] !== 255) {
        p++
        continue
      }
      const marker = bytes[p + 1]!
      if (marker === 216 || marker === 217) {
        p += 2
        continue
      }
      const length = v.getUint16(p + 2)
      if (length < 2) break
      if ([192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207].includes(marker))
        return { width: v.getUint16(p + 7), height: v.getUint16(p + 5) }
      p += length + 2
    }
  }
  return null
}
export async function prepareEvidence(file: File): Promise<PreparedEvidence> {
  const policy = reference.evidence
  if (!policy) throw Error('Evidence settings are unavailable. Please reload the portal.')
  const limitLabel = (bytes: number) => (bytes / (1024 * 1024)).toLocaleString() + ' MB'
  if (file.size === 0) throw Error('Choose a file that contains evidence.')
  if (/\.(heic|heif)$/i.test(file.name) || /image\/hei[cf]/i.test(file.type))
    throw Error('HEIC/HEIF is not supported. Export the photo as JPEG, then attach it again.')
  if (/\.pdf$/i.test(file.name)) {
    if (!policy.allowedExtensions.includes('.pdf')) throw Error('PDF evidence is not accepted.')
    if (file.size > policy.maximumFileBytes)
      throw Error(`PDFs must be ${limitLabel(policy.maximumFileBytes)} or smaller.`)
    return { file }
  }
  const extension = '.' + file.name.split('.').at(-1)?.toLowerCase()
  if (!policy.allowedExtensions.includes(extension) || !/\.(jpe?g|png|webp)$/i.test(file.name))
    throw Error('Choose PDF, JPEG, PNG or WebP evidence.')
  if (file.size > policy.maximumOriginalBytes)
    throw Error(
      `Photos must be ${limitLabel(policy.maximumOriginalBytes)} or smaller before optimisation.`
    )
  const dimensions = inspectImageDimensions(
    new Uint8Array(await file.slice(0, 262144).arrayBuffer())
  )
  if (
    !dimensions ||
    dimensions.width < 1 ||
    dimensions.height < 1 ||
    dimensions.width * dimensions.height > 40_000_000 ||
    dimensions.width > 12000 ||
    dimensions.height > 12000
  )
    throw Error('Use a supported still image up to 40 megapixels.')
  if (
    file.size <= Math.min(2 * 1024 * 1024, policy.maximumFileBytes) &&
    Math.max(dimensions.width, dimensions.height) <= 2400
  )
    return { file }
  const image = await decodeEvidenceImage(file),
    canvas = document.createElement('canvas')
  try {
    const scale = Math.min(1, 2400 / Math.max(image.width, image.height))
    canvas.width = Math.max(1, Math.round(image.width * scale))
    canvas.height = Math.max(1, Math.round(image.height * scale))
    const context = canvas.getContext('2d')
    if (!context) throw Error('Image optimisation is unavailable. Export a smaller JPEG photo.')
    context.fillStyle = '#ffffff'
    context.fillRect(0, 0, canvas.width, canvas.height)
    context.drawImage(image.source, 0, 0, canvas.width, canvas.height)
    const blob = await new Promise<Blob>((resolve, reject) =>
      canvas.toBlob(
        (value) =>
          value ? resolve(value) : reject(Error('Photo optimisation failed. Export a JPEG copy.')),
        'image/jpeg',
        0.88
      )
    )
    if (blob.size > policy.maximumFileBytes)
      throw Error('This photo remains too large. Export a smaller JPEG copy.')
    return {
      file: new File([blob], file.name.replace(/\.[^.]+$/, '') + '-optimized.jpg', {
        type: 'image/jpeg',
        lastModified: file.lastModified
      }),
      original: file
    }
  } finally {
    image.close()
    canvas.width = 0
    canvas.height = 0
  }
}
