import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import { createApiProxy } from './config/apiProxy.mjs'
import { previewHttps } from './config/previewHttps.mjs'
import { publicBrandIcons } from './config/publicBrandIcons.mjs'

const https = previewHttps()
const secureHeaders = https
  ? {
      'Cache-Control': 'private, no-store',
      'X-Content-Type-Options': 'nosniff',
      'X-Frame-Options': 'DENY',
      'Referrer-Policy': 'no-referrer',
      'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
      'Content-Security-Policy':
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' wss:; manifest-src 'self'; worker-src 'self' blob:; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'"
    }
  : undefined
const portIndex = process.argv.indexOf('--port')
const cachePort =
  portIndex >= 0 && /^\d+$/.test(process.argv[portIndex + 1] ?? '')
    ? process.argv[portIndex + 1]
    : 'default'
export default defineConfig({
  cacheDir: 'node_modules/.vite-' + cachePort,
  plugins: [
    publicBrandIcons(),
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: null,
      includeAssets: ['app-icon.svg', 'icons/*.png'],
      manifest: {
        id: '/',
        scope: '/',
        name: 'Dispute Portal',
        short_name: 'Dispute Portal',
        description: 'Review transactions and track dispute progress.',
        theme_color: '#e7352b',
        background_color: '#f5f8fa',
        display: 'standalone',
        start_url: '/',
        icons: [
          {
            src: '/icons/dispute-shield-v7-192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any'
          },
          {
            src: '/icons/dispute-shield-v7-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any'
          },
          {
            src: '/icons/dispute-shield-v7-maskable-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable'
          }
        ]
      },
      workbox: {
        importScripts: ['/push-events.js'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api(?:\/|$)/, /^\/install(?:\/|$)/],
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        runtimeCaching: [
          {
            urlPattern: ({ url }) => url.pathname.startsWith('/api/'),
            handler: 'NetworkOnly',
            options: { cacheName: 'private-api-never-cache' }
          }
        ]
      }
    })
  ],
  server: {
    watch: { ignored: ['**/.vs/**'] },
    https,
    port: 5173,
    proxy: { '/api': createApiProxy() }
  },
  build: { target: 'esnext' },
  preview: { https, headers: secureHeaders, host: '0.0.0.0', proxy: { '/api': createApiProxy() } }
})
