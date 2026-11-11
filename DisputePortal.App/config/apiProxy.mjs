import process from 'node:process'

// Vite configuration only: this module is never imported by the client bundle.
export function createApiProxy(target = process.env.PORTAL_API_ORIGIN ?? 'http://localhost:8081') {
  return {
    target,
    secure: true,
    changeOrigin: true,
    ws: true,
    xfwd: true,
    configure(proxy) {
      // Replace caller-supplied chains at this single trusted proxy boundary.
      // Origin remains untouched so the API can validate the browser origin.
      const recordSocket = (outgoing, incoming) => {
        const address = incoming.socket.remoteAddress
        if (address) outgoing.setHeader('X-Forwarded-For', address)
        else outgoing.removeHeader('X-Forwarded-For')
        outgoing.setHeader('X-Forwarded-Proto', incoming.socket.encrypted ? 'https' : 'http')
      }
      proxy.on('proxyReq', recordSocket)
      proxy.on('proxyReqWs', recordSocket)
    }
  }
}
