// The Worker's behaviour, with Cloudflare's API faked:  cd Tools/turn-worker && npm test
import test from 'node:test'
import assert from 'node:assert/strict'
import worker, {usable} from './src/index.js'

const env = {TURN_KEY_ID: 'key-id', TURN_KEY_API_TOKEN: 'token', ALLOWED_ORIGINS: 'https://deanstephens.github.io, http://localhost:8790'}
// Cloudflare's example answer: STUN, then TURN and TURNS on several ports, one of them 53.
const cloudflare = {iceServers: [
  {urls: ['stun:stun.cloudflare.com:3478', 'stun:stun.cloudflare.com:53']},
  {urls: ['turn:turn.cloudflare.com:3478?transport=udp', 'turn:turn.cloudflare.com:53?transport=udp',
    'turn:turn.cloudflare.com:3478?transport=tcp', 'turns:turn.cloudflare.com:5349?transport=tcp', 'turns:turn.cloudflare.com:443?transport=tcp'],
   username: 'u', credential: 'c'},
]}

let calls = []
globalThis.fetch = async (url, init) => {
  calls.push({url, init})
  return new Response(JSON.stringify(cloudflare), {status: 201})
}
const ask = (headers = {}, method = 'GET') => worker.fetch(new Request('https://facecast-turn.example.workers.dev/', {method, headers}), env)

test('the site gets ICE servers, with Cloudflare asked properly', async () => {
  calls = []
  const res = await ask({Origin: 'https://deanstephens.github.io'})
  assert.equal(res.status, 200)
  assert.equal(res.headers.get('Access-Control-Allow-Origin'), 'https://deanstephens.github.io')
  assert.equal(res.headers.get('Cache-Control'), 'no-store')
  const {iceServers} = await res.json()
  assert.equal(iceServers[1].username, 'u')
  assert.ok(iceServers.flatMap(s => s.urls).every(u => !/:53(\?|$)/.test(u)), 'no port 53')
  assert.ok(iceServers.flatMap(s => s.urls).includes('turns:turn.cloudflare.com:5349?transport=tcp'), 'but 5349 stays')
  assert.equal(calls.length, 1)
  assert.equal(calls[0].url, 'https://rtc.live.cloudflare.com/v1/turn/keys/key-id/credentials/generate-ice-servers')
  assert.equal(calls[0].init.method, 'POST')
  assert.equal(calls[0].init.headers.Authorization, 'Bearer token')
  assert.equal(JSON.parse(calls[0].init.body).ttl, 43200)
})

test('other websites are turned away; tools without a page are not', async () => {
  calls = []
  assert.equal((await ask({Origin: 'https://elsewhere.example'})).status, 403)
  assert.equal(calls.length, 0, 'without asking Cloudflare')
  assert.equal((await ask()).status, 200)
  const preflight = await ask({Origin: 'http://localhost:8790'}, 'OPTIONS')
  assert.equal(preflight.status, 204)
  assert.equal(preflight.headers.get('Access-Control-Allow-Origin'), 'http://localhost:8790')
})

test('missing secrets and Cloudflare failing are reported', async () => {
  assert.equal((await worker.fetch(new Request('https://x/'), {})).status, 500)
  globalThis.fetch = async () => new Response('nope', {status: 401})
  const res = await ask()
  assert.equal(res.status, 502)
  assert.match((await res.json()).error, /401/)
})

test('port 53 URLs are dropped, and servers left with none', () => {
  assert.deepEqual(usable([{urls: 'turn:a:53'}, {urls: ['turn:a:3478', 'turn:a:53?transport=udp']}]), [{urls: ['turn:a:3478']}])
  assert.deepEqual(usable(undefined), [])
  assert.deepEqual(usable({urls: 'stun:a:3478'}), [{urls: ['stun:a:3478']}])
})
