// Facecast's TURN credentials, from Cloudflare (Realtime TURN). Facecast is a static site, and Cloudflare only hands
// out TURN credentials to someone holding the TURN key, which mustn't be public. This Worker holds the key (as
// secrets), asks Cloudflare for credentials that last a while, and gives the page the ICE servers: config.js's turnUrl
// points here. Deploy it with wrangler (see ../../README.md, "TURN through Cloudflare").
//
// Secrets:  TURN_KEY_ID, TURN_KEY_API_TOKEN   (npx wrangler secret put …)
// Vars:     ALLOWED_ORIGINS (wrangler.toml)  pages that may ask, comma separated

const CLOUDFLARE = 'https://rtc.live.cloudflare.com/v1/turn/keys'
// Long enough for a long meeting: a connection relayed through TURN needs its credentials to stay good.
const TTL_SECONDS = 12 * 60 * 60

export default {
  async fetch(request, env) {
    const origin = request.headers.get('Origin')
    const allowed = (env.ALLOWED_ORIGINS || '').split(',').map(s => s.trim()).filter(Boolean)
    const cors = origin && allowed.includes(origin)
      ? {'Access-Control-Allow-Origin': origin, 'Access-Control-Allow-Methods': 'GET', Vary: 'Origin'}
      : {}
    // Other websites' pages may not use it (a browser always says which page is asking). Tools without a page
    // (npm run turn-check, curl) may.
    if (origin && !cors['Access-Control-Allow-Origin']) return reply({error: 'this page may not use these TURN servers'}, 403)
    if (request.method === 'OPTIONS') return new Response(null, {status: 204, headers: cors})
    if (request.method !== 'GET') return reply({error: 'GET only'}, 405, cors)
    if (!env.TURN_KEY_ID || !env.TURN_KEY_API_TOKEN) return reply({error: 'the TURN key secrets are not set'}, 500, cors)

    let res
    try {
      res = await fetch(`${CLOUDFLARE}/${env.TURN_KEY_ID}/credentials/generate-ice-servers`, {
        method: 'POST',
        headers: {Authorization: `Bearer ${env.TURN_KEY_API_TOKEN}`, 'Content-Type': 'application/json'},
        body: JSON.stringify({ttl: TTL_SECONDS}),
      })
    } catch (e) {
      return reply({error: `couldn't reach Cloudflare: ${e.message}`}, 502, cors)
    }
    if (!res.ok) return reply({error: `Cloudflare said ${res.status}`}, 502, cors)
    const data = await res.json()
    return reply({iceServers: usable(data.iceServers)}, 200, cors)
  },
}

// Cloudflare's servers, less the port 53 ones, which browsers block (they only time out).
export function usable(iceServers) {
  return [].concat(iceServers || []).map(s => ({
    ...s,
    urls: [].concat(s.urls || []).filter(u => !/:53(\?|$)/.test(u)),
  })).filter(s => s.urls.length)
}

function reply(body, status, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {'Content-Type': 'application/json', 'Cache-Control': 'no-store', ...headers},
  })
}
