import { createSSRApp, h } from 'vue'
import { renderToString } from 'vue/server-renderer'
import { ShieldCheck } from '@lucide/vue'
import { createCanvas, loadImage } from '@napi-rs/canvas'
import { Buffer } from 'node:buffer'
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'

export const brandRed = '#e7352b'
export const iconRevision = 'shield-v7'
export const iconFiles = {
  standard192: 'dispute-shield-v7-192.png',
  standard512: 'dispute-shield-v7-512.png',
  maskable: 'dispute-shield-v7-maskable-512.png',
  apple: 'dispute-shield-v7-apple-180.png',
  badge: 'dispute-shield-v7-badge.png'
}

// AppShell renders this same ShieldCheck at 25px inside its 38px mobile mark.
// Rendering the component preserves its exact paths, stroke, caps and joins;
// there is no second hand-drawn shield/check geometry.
export async function brandSvg(background = true) {
  const glyph = await renderToString(
    createSSRApp({ render: () => h(ShieldCheck, { size: 25, color: '#fff', strokeWidth: 2 }) })
  )
  const positioned = glyph.replace('<svg ', '<svg x="6.5" y="6.5" ')
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 38 38">' +
    (background ? '<rect width="38" height="38" rx="11" fill="' + brandRed + '"/>' : '') +
    positioned +
    '</svg>\n'
  )
}

export async function brandIconAssets() {
  const assets = new Map()
  const svg = await brandSvg()
  assets.set('app-icon.svg', Buffer.from(svg))
  const image = await loadImage(Buffer.from(svg))
  const variants = [
    [iconFiles.standard192, 192, 'resolve-192.png'],
    [iconFiles.standard512, 512, 'resolve-512.png'],
    [iconFiles.maskable, 512, 'resolve-maskable-512.png'],
    [iconFiles.apple, 180, 'resolve-apple-180.png']
  ]
  for (const [name, size, previous] of variants) {
    const canvas = createCanvas(size, size),
      context = canvas.getContext('2d')
    // Full-bleed opaque red is required for Home Screen/system masks.
    context.fillStyle = brandRed
    context.fillRect(0, 0, size, size)
    context.drawImage(image, 0, 0, size, size)
    const bytes = canvas.toBuffer('image/png')
    assets.set('icons/' + name, bytes)
    assets.set('icons/' + previous, bytes)
  }
  assets.set('apple-touch-icon.png', assets.get('icons/' + iconFiles.apple))
  assets.set('apple-touch-icon-precomposed.png', assets.get('icons/' + iconFiles.apple))
  const badge = createCanvas(96, 96),
    context = badge.getContext('2d')
  context.drawImage(await loadImage(Buffer.from(await brandSvg(false))), 0, 0, 96, 96)
  const bytes = badge.toBuffer('image/png')
  assets.set('icons/' + iconFiles.badge, bytes)
  assets.set('icons/resolve-badge.png', bytes)
  return assets
}

async function main() {
  const output = resolve(dirname(fileURLToPath(import.meta.url)), '../public')
  mkdirSync(resolve(output, 'icons'), { recursive: true })
  for (const [name, bytes] of await brandIconAssets()) {
    writeFileSync(resolve(output, name), bytes)
    process.stdout.write('Generated ' + name + '\n')
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  main().catch((error) => {
    process.stderr.write(error.message + '\n')
    process.exitCode = 1
  })
