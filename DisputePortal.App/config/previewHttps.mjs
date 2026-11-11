import { readFileSync } from 'node:fs'
import { isAbsolute } from 'node:path'
import process from 'node:process'
import { createSecureContext } from 'node:tls'

// Node/Vite configuration only. These buffers never enter the browser bundle.
export function previewHttps(environment = process.env) {
  const enabled = environment.PORTAL_HTTPS_ENABLED
  if (!enabled || enabled === '0' || enabled === 'false') return undefined
  if (enabled !== '1' && enabled !== 'true') throw Error('PORTAL_HTTPS_ENABLED must be 1 or 0.')
  const certificate = environment.PORTAL_HTTPS_CERT_FILE
  const privateKey = environment.PORTAL_HTTPS_KEY_FILE
  if (!certificate || !privateKey || !isAbsolute(certificate) || !isAbsolute(privateKey))
    throw Error('HTTPS preview requires absolute certificate and key file paths.')
  try {
    const options = {
      cert: readFileSync(certificate),
      key: readFileSync(privateKey),
      minVersion: 'TLSv1.2'
    }
    createSecureContext(options)
    return options
  } catch {
    throw Error('HTTPS certificate/key could not be read or validated; HTTP fallback is disabled.')
  }
}
