import { readFileSync, existsSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { fileURLToPath, URL } from 'node:url'
import { resolve } from 'node:path'

// Only public brand PNGs may be retained by the device's Home Screen icon loader.
// API, document and HTML responses retain their existing no-store policy.
export function publicBrandIcons() {
  return {
    name: 'public-brand-icons',
    configurePreviewServer(server) {
      const directory = fileURLToPath(new URL('../public/', import.meta.url))
      server.middlewares.use((request, response, next) => {
        const path = request.url?.split('?')[0]
        if (
          !path ||
          !(
            /^\/icons\/(?:dispute|resolve)-[a-z0-9-]+\.png$/.test(path) ||
            path === '/apple-touch-icon.png' ||
            path === '/apple-touch-icon-precomposed.png'
          ) ||
          !['GET', 'HEAD'].includes(request.method)
        ) {
          next()
          return
        }
        const file = resolve(directory, '.' + path)
        if (!existsSync(file)) {
          next()
          return
        }
        const bytes = readFileSync(file)
        if (bytes.length > 1048576) {
          next()
          return
        }
        const etag = '"' + createHash('sha256').update(bytes).digest('hex') + '"'
        const headers = {
          'Cache-Control': 'public, max-age=3600',
          ETag: etag,
          'Content-Type': 'image/png',
          'X-Content-Type-Options': 'nosniff',
          'X-Frame-Options': 'DENY',
          'Referrer-Policy': 'no-referrer',
          'Content-Security-Policy': "default-src 'none'; frame-ancestors 'none'"
        }
        if (request.headers['if-none-match'] === etag) {
          response.writeHead(304, headers)
          response.end()
          return
        }
        response.writeHead(200, { ...headers, 'Content-Length': String(bytes.length) })
        response.end(request.method === 'HEAD' ? undefined : bytes)
      })
    }
  }
}
